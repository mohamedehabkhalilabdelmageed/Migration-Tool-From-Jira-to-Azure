using Atlassian.Jira;
using Common.Config;
using JiraExport.RevisionUtils;
using Migration.Common;
using Migration.Common.Config;
using Migration.Common.Log;
using Migration.WIContract;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace JiraExport
{
    public static class FieldMapperUtils
    {
        public static object MapRemainingWork(string seconds)
        {
            var secs = 0d;
            try
            {
                if (seconds == null)
                {
                    throw new FormatException();
                }
                secs = Convert.ToDouble(seconds);
            }
            catch (FormatException)
            {
                Logger.Log(LogLevel.Warning, $"A FormatException was thrown when converting RemainingWork value '{seconds}' to double. Defaulting to RemainingWork = null.");
                return null;
            }
            return TimeSpan.FromSeconds(secs).TotalHours;
        }

        public static (bool, object) MapTitle(JiraRevision r)
        {
            if (r == null)
                throw new ArgumentNullException(nameof(r));

            if (r.Fields.TryGetValue("summary", out object summary))
                return (true, $"[{r.ParentItem.Key}] {summary}");
            else
                return (false, null);
        }
        public static (bool, object) MapTitleWithoutKey(JiraRevision r)
        {
            if (r == null)
                throw new ArgumentNullException(nameof(r));

            if (r.Fields.TryGetValue("summary", out object summary))
                return (true, summary);
            else
                return (false, null);
        }

        public static (bool, object) MapValue(JiraRevision r, string itemSource, string itemTarget, ConfigJson config, ExportIssuesSummary exportIssuesSummary)
        {
            if (r == null)
                throw new ArgumentNullException(nameof(r));

            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var targetWit = (from t in config.TypeMap.Types where t.Source == r.Type select t.Target).FirstOrDefault();

            var hasFieldValue = r.Fields.TryGetValue(itemSource, out object value);

            if (!hasFieldValue)
                return (false, null);

            foreach (var item in config.FieldMap.Fields.Where(i => i.Mapping?.Values != null))
            {
                var sourceAndTargetMatch = item.Source == itemSource && item.Target == itemTarget;
                var forOrAllMatch = item.For.Contains(targetWit) || item.For == "All";  // matches "For": "All", or when this Wit is specifically named.
                var notForMatch = !string.IsNullOrWhiteSpace(item.NotFor) && !item.NotFor.Contains(targetWit);  // matches if not-for is specified and doesn't contain this Wit.

                if (sourceAndTargetMatch && (forOrAllMatch || notForMatch))
                {
                    if (value == null)
                    {
                        return (true, null);
                    }
                    var mappedValue = (from s in item.Mapping.Values where s.Source == value.ToString() select s.Target).FirstOrDefault();
                    if (string.IsNullOrEmpty(mappedValue))
                    {
                        Logger.Log(LogLevel.Warning, $"Missing mapping value '{value}' for field '{itemSource}' for item type '{targetWit}'.");
                        if(itemSource == "status")
                        {
                            exportIssuesSummary.AddUnmappedIssueState(targetWit, value.ToString());
                        }
                    }
                    return (true, mappedValue);
                }
            }
            return (true, value);
        }

        public static (bool, object) MapRenderedValue(JiraRevision r, string sourceField, bool isCustomField, string customFieldName, ConfigJson config)
        {
            if (r == null)
                throw new ArgumentNullException(nameof(r));

            if (config == null)
                throw new ArgumentNullException(nameof(config));

            // Support fallback sources separated by '|', first one found wins
            var candidateSources = (sourceField ?? string.Empty).Split('|');

            object value = null;
            string fieldName = null;
            bool hasFieldValue = false;
            foreach (var candidate in candidateSources)
            {
                var trimmed = candidate?.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                var resolved = SetCustomFieldName(trimmed, isCustomField, customFieldName);
                fieldName = resolved + "$Rendered";
                hasFieldValue = r.Fields.TryGetValue(fieldName, out value);
                if (hasFieldValue) break;
            }

            var targetWit = (from t in config.TypeMap.Types where t.Source == r.Type select t.Target).FirstOrDefault();

            if (!hasFieldValue)
                return (false, null);

            foreach (var item in config.FieldMap.Fields)
            {
                if (((item.Source == fieldName && (item.For.Contains(targetWit) || item.For == "All")) ||
                      item.Source == fieldName && (!string.IsNullOrWhiteSpace(item.NotFor) && !item.NotFor.Contains(targetWit))) &&
                      item.Mapping?.Values != null)
                {
                    var mappedValue = (from s in item.Mapping.Values where s.Source == value.ToString() select s.Target).FirstOrDefault();
                    if (string.IsNullOrEmpty(mappedValue))
                    {
                        Logger.Log(LogLevel.Warning, $"Missing mapping value '{value}' for field '{fieldName}'.");
                    }
                    return (true, mappedValue);
                }
            }
            value = CorrectRenderedHtmlvalue(value, r, config.IncludeJiraCssStyles);

            return (true, value);
        }

        public static (bool, object) MapTestStepsValue(JiraRevision r, string sourceField, bool isCustomField, string customFieldName, IJiraProvider jiraProvider)
        {
            if (r == null)
                throw new ArgumentNullException(nameof(r));

            var candidateSources = (sourceField ?? string.Empty).Split('|');
            var singleConfiguredSource = candidateSources.Length == 1
                && !string.IsNullOrWhiteSpace(candidateSources[0]);

            if (jiraProvider != null && !string.IsNullOrWhiteSpace(jiraProvider.GetSettings().XrayClientId))
            {
                var xrayStepsJson = jiraProvider.GetXrayTestStepsGraphQL(r.ParentItem.Id ?? r.OriginId);
                if (!string.IsNullOrWhiteSpace(xrayStepsJson))
                {
                    var stepsXml = BuildAzureTestStepsXml(xrayStepsJson);
                    if (!string.IsNullOrWhiteSpace(stepsXml))
                        return (true, stepsXml);
                }
            }

            foreach (var candidate in candidateSources)
            {
                var trimmed = candidate?.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                var resolved = ResolveStepSourceFieldKey(trimmed, isCustomField, customFieldName, singleConfiguredSource, jiraProvider);
                var renderedField = resolved + "$Rendered";
                if (r.Fields.TryGetValue(renderedField, out object renderedValue))
                {
                    var stepsXml = BuildAzureTestStepsXml(renderedValue?.ToString());
                    return string.IsNullOrWhiteSpace(stepsXml) ? (false, null) : (true, stepsXml);
                }

                if (r.Fields.TryGetValue(resolved, out object rawValue))
                {
                    var stepsXml = BuildAzureTestStepsXml(rawValue?.ToString());
                    return string.IsNullOrWhiteSpace(stepsXml) ? (false, null) : (true, stepsXml);
                }
            }

            return (false, null);
        }

        private static string ResolveStepSourceFieldKey(string candidate, bool isCustomField, string customFieldName, bool singleConfiguredSource, IJiraProvider jiraProvider)
        {
            if (jiraProvider != null)
            {
                var id = jiraProvider.GetCustomId(candidate);
                if (!string.IsNullOrEmpty(id))
                    return id;
            }

            if (isCustomField && singleConfiguredSource && !string.IsNullOrEmpty(customFieldName))
                return customFieldName;

            return candidate;
        }



        public static object MapTags(string labels)
        {
            if (labels == null)
                throw new ArgumentNullException(nameof(labels));

            if (string.IsNullOrWhiteSpace(labels))
                return string.Empty;

            var tags = labels.Split(' ');
            if (!tags.Any())
                return string.Empty;
            else
                return string.Join(";", tags);
        }

        public static object MapArray(string field)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));

            if (string.IsNullOrWhiteSpace(field))
                return null;

            var values = field.Split(',');
            if (!values.Any())
                return null;
            else
                return string.Join(";", values);
        }

        public static object MapSprint(string iterationPathsString)
        {
            if (string.IsNullOrWhiteSpace(iterationPathsString))
                return null;

            // For certain configurations of Jira, the entire Sprint object is returned by the
            // fields Rest API instead of the Sprint name
            if (iterationPathsString.StartsWith("com.atlassian.greenhopper.service.sprint.Sprint@"))
            {
                Regex regex = new Regex(@",name=([^,]+),");
                Match match = regex.Match(iterationPathsString);
                if (match.Success)
                {
                    iterationPathsString = match.Groups[1].Value;
                }
                else
                {
                    Logger.Log(LogLevel.Error, "Missing 'name' property for Sprint object. "
                        + $"Skipping mapping this sprint. The full object was: '{iterationPathsString}'."
                        );
                }
            }

            var iterationPaths = iterationPathsString.Split(',').AsEnumerable();
            iterationPaths = iterationPaths.Select(ip => ip.Trim());
            var iterationPath = iterationPaths.Last();

            iterationPath = ReplaceAzdoInvalidCharacters(iterationPath);

            // Remove leading and trailing spaces, since these will be stripped by the Azure DevOps classification nodes Rest API
            iterationPath = iterationPath.Trim();

            return iterationPath;
        }

        private static readonly Dictionary<string, decimal> CalculatedLexoRanks = new Dictionary<string, decimal>();
        private static readonly Dictionary<decimal, string> CalculatedRanks = new Dictionary<decimal, string>();

        private static readonly Regex LexoRankRegex = new Regex(@"^[0-2]\|[0-9a-zA-Z]*(\:[0-9a-zA-Z]*)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

        public static object MapLexoRank(string lexoRank)
        {
            if (string.IsNullOrEmpty(lexoRank) || !LexoRankRegex.IsMatch(lexoRank))
                return decimal.MaxValue;

            if (CalculatedLexoRanks.ContainsKey(lexoRank))
            {
                Logger.Log(LogLevel.Warning, "Duplicate rank detected. You may need to re-balance the JIRA LexoRank. see: https://confluence.atlassian.com/adminjiraserver/managing-lexorank-938847803.html");
                return CalculatedLexoRanks[lexoRank];
            }

            // split by bucket and sub-rank delimiters
            var lexoSplit = lexoRank.Split(new[] {'|', ':'}, StringSplitOptions.RemoveEmptyEntries);

            // calculate the numeric value of the rank and sub-rank (if available)
            var b36Rank = Base36.Decode(lexoSplit[1]);
            var b36SubRank = lexoSplit.Length == 3 && !string.IsNullOrEmpty(lexoSplit[2])
                ? Base36.Decode(lexoSplit[2])
                : 0L;

            // calculate final rank value
            var rank = Math.Round(
                Convert.ToDecimal($"{b36Rank}.{b36SubRank}", CultureInfo.InvariantCulture.NumberFormat),
                7 // DevOps seems to ignore anything over 7 decimal places long
            );

            if (CalculatedRanks.ContainsKey(rank) && CalculatedRanks[rank] != lexoRank)
            {
                Logger.Log(LogLevel.Warning, "Duplicate rank detected for different LexoRank values. You may need to re-balance the JIRA LexoRank. see: https://confluence.atlassian.com/adminjiraserver/managing-lexorank-938847803.html");
            }
            else
            {
                CalculatedRanks.Add(rank, lexoRank);
            }

            CalculatedLexoRanks.Add(lexoRank, rank);
            return rank;
        }

        public static string CorrectRenderedHtmlvalue(object value, JiraRevision revision, bool includeJiraStyle)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (revision == null)
                throw new ArgumentNullException(nameof(revision));

            var htmlValue = value.ToString();

            if (string.IsNullOrWhiteSpace(htmlValue))
                return htmlValue;

            foreach (var attUrl in revision.AttachmentActions.Where(aa => aa.ChangeType == RevisionChangeType.Added).Select(aa => aa.Value.Url))
            {
                if (!string.IsNullOrWhiteSpace(attUrl) && htmlValue.Contains(attUrl))
                    htmlValue = htmlValue.Replace(attUrl, attUrl);
            }

            htmlValue = RevisionUtility.ReplaceHtmlElements(htmlValue);

            if (includeJiraStyle)
            {
                string css = ReadEmbeddedFile("JiraExport.jirastyles.css");
                if (string.IsNullOrWhiteSpace(css))
                    Logger.Log(LogLevel.Warning, $"Could not read css styles for rendered field in {revision.OriginId}.");
                else
                    htmlValue = "<style>" + css + "</style>" + htmlValue;
            }

            return htmlValue;
        }

        private static string ReadEmbeddedFile(string resourceName)
        {
            var assembly = Assembly.GetEntryAssembly();

            try
            {
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                using (StreamReader reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (ArgumentNullException)
            {
                return "";
            }
        }
        private static string SetCustomFieldName(string sourceField, bool isCustomField, string customFieldName)
        {
            if (isCustomField)
            {
                sourceField = customFieldName;
            }

            return sourceField;
        }

        private static string ReplaceAzdoInvalidCharacters(string inputString)
        {
            return Regex.Replace(inputString, "[/$?*:\"&<>#%|+]", "", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        }

        private static string BuildAzureTestStepsXml(string source)
        {
            var steps = ExtractStepPairs(source);
            if (steps.Count == 0)
                return null;

            var sb = new StringBuilder();
            sb.Append("<steps id=\"0\" last=\"");
            sb.Append(steps.Count + 1);
            sb.Append("\">");

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var action = SecurityElement.Escape(step.Action ?? string.Empty) ?? string.Empty;
                var expected = SecurityElement.Escape(step.Expected ?? string.Empty) ?? string.Empty;
                sb.Append("<step id=\"");
                sb.Append(i + 2);
                sb.Append("\" type=\"ActionStep\">");
                sb.Append("<parameterizedString isformatted=\"true\">");
                sb.Append(action);
                sb.Append("</parameterizedString>");
                sb.Append("<parameterizedString isformatted=\"true\">");
                sb.Append(expected);
                sb.Append("</parameterizedString>");
                sb.Append("<description/>");
                sb.Append("</step>");
            }

            sb.Append("</steps>");
            return sb.ToString();
        }

        private static List<(string Action, string Expected)> ExtractStepPairs(string source)
        {
            var result = new List<(string Action, string Expected)>();
            if (string.IsNullOrWhiteSpace(source))
                return result;

            var trimmed = source.Trim();
            var jsonSteps = TryExtractStepsFromJson(trimmed);
            if (jsonSteps.Count > 0)
                return jsonSteps;

            var tableRowRegex = new Regex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var tableCellRegex = new Regex(@"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var rowMatches = tableRowRegex.Matches(source);
            foreach (Match row in rowMatches)
            {
                var cells = tableCellRegex.Matches(row.Groups[1].Value)
                    .Cast<Match>()
                    .Select(m => HtmlToPlainText(m.Groups[1].Value))
                    .ToList();

                if (cells.All(string.IsNullOrWhiteSpace))
                    continue;

                if (cells.Count >= 3)
                {
                    var combinedAction = cells[0];
                    if (!string.IsNullOrWhiteSpace(cells[1]))
                    {
                        if (!string.IsNullOrWhiteSpace(combinedAction))
                            combinedAction += "\n\nData:\n" + cells[1];
                        else
                            combinedAction = cells[1];
                    }
                    result.Add((combinedAction, cells[2]));
                }
                else if (cells.Count == 2)
                {
                    result.Add((cells[0], cells[1]));
                }
                else if (cells.Count == 1)
                {
                    result.Add((cells[0], string.Empty));
                }
            }

            if (result.Count > 0)
                return result;

            var plain = HtmlToPlainText(source);
            var lines = plain
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => Regex.Replace(l, @"^\s*(\d+[\.\)]|[-*])\s*", "").Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            foreach (var line in lines)
                result.Add((line, string.Empty));

            return result;
        }

        private static List<(string Action, string Expected)> TryExtractStepsFromJson(string source)
        {
            var output = new List<(string Action, string Expected)>();
            if (!(source.StartsWith("{") || source.StartsWith("[")))
                return output;

            try
            {
                var token = JToken.Parse(source);
                var candidates = token.Type == JTokenType.Array ? (JArray)token : token.SelectToken("$.steps") as JArray;
                if (candidates == null)
                    return output;

                foreach (var step in candidates)
                {
                    var actionObj = step["action"]?.ToString() ?? step["step"]?.ToString();
                    var dataObj = step["data"]?.ToString();
                    var expectedObj = step["result"]?.ToString()
                                   ?? step["expectedResult"]?.ToString()
                                   ?? step["expected"]?.ToString();

                    var combinedAction = string.Empty;
                    if (!string.IsNullOrWhiteSpace(actionObj))
                        combinedAction += HtmlToPlainText(actionObj);
                        
                    if (!string.IsNullOrWhiteSpace(dataObj))
                    {
                        var dataPlain = HtmlToPlainText(dataObj);
                        if (!string.IsNullOrWhiteSpace(dataPlain))
                        {
                            if (!string.IsNullOrWhiteSpace(combinedAction))
                                combinedAction += "\n\nData:\n" + dataPlain;
                            else
                                combinedAction = dataPlain;
                        }
                    }

                    var expected = string.Empty;
                    if (!string.IsNullOrWhiteSpace(expectedObj))
                    {
                        expected = HtmlToPlainText(expectedObj);
                    }

                    if (!string.IsNullOrWhiteSpace(combinedAction) || !string.IsNullOrWhiteSpace(expected))
                    {
                        output.Add((combinedAction, expected));
                    }
                }
            }
            catch
            {
                return output;
            }

            return output;
        }

        private static string HtmlToPlainText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var text = Regex.Replace(input, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</p\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<.*?>", string.Empty);
            text = HttpUtility.HtmlDecode(text);
            return text?.Trim() ?? string.Empty;
        }
    }

}
