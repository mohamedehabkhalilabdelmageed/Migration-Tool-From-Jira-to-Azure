# Jira to Azure DevOps work item migration tool

The Jira to Azure DevOps work item migration tool lets you export data from Jira and import it as work items in Azure DevOps or Microsoft Team Foundation Server.

## Features

This tool lets you migrate work item from Jira to Azure DevOps or Microsoft Team Foundation Server. The tool has two parts, first Jira issues are exported to files then we import the files to Azure DevOps/TFS. We believe this is a good approach to migration since it allows you to export the data to migrate and validate it before importing it. It also makes it easy to migrate in batches.

Some of the capabilities include:

- Export Jira issues from Jira queries
- Map users from Jira to users in Azure DevOps/TFS
- Migrate work item field data
- Migrate links and attachments
- Migrate history
- **New!** Seamlessly migrate **Xray Test Cases** with their manual test steps (Action, Data, Expected Result) directly to Azure DevOps Test Plans via the Xray Cloud GraphQL API.

## Xray Cloud Integration

In order to migrate manual test steps from Xray Cloud to Azure DevOps, the migrator requires authentication with the Xray GraphQL API. 

To enable this feature, simply generate an API Key in your Xray Cloud settings and append the following properties to the root of your `config.json` file:

```json
{
  "xray-client-id": "YOUR_XRAY_CLIENT_ID",
  "xray-client-secret": "YOUR_XRAY_CLIENT_SECRET",
  ...
}
```
With these credentials provided, the migrator will automatically extract the `Action`, `Data`, and `Expected Result` fields from your Xray test steps and format them natively for Azure DevOps.

### Products

The **Jira Azure DevOps Migrator** 

The following list contains all of the products on offer:

- Jira Azure DevOps Migrator
- Jira Azure DevOps Migrator Bootstrapper
- Jira Test Management Migrator (XRay, Zephyr, QMetry + more)
- Confluence to Azure DevOps Wikis Migrator

### Jira Azure DevOps Migrator

- Migrate **Releases** and the **Fixes Version** and **Affects Version** fields
  - Release date, start date, release status and release description
- Migrate **Branch links** from Bitbucket to Azure DevOps.
- Migrate **Sprint Dates**.
- Composite field mapper (consolidate multiple Jira fields into a single ADO field)
- Migrate **Remote Links** (Web links) to Work Item hyperlinks.
- Correct any **Embedded Links to Jira Issues** in text fields such as Description, Repro Steps and comments, so that they point to the correct Work Item in Azure DevOps.
- Support for state transition dates (e.g. `ActivatedDate`, `ClosedDate`) for workflows with custom states. By default, only **New**, **Closed** and **Done** are supported.
- Select any property for **object**- and **array**-type fields for mapping. This allows for:
  - More possibilities when mapping the **Fixes Version**, **Affects Version** fields and **Components** fields.
  - More possibilities when **mapping Azure DevOps **custom**

### Jira Azure DevOps Migrator Bootstrapper

The **Jira Azure DevOps Migrator Bootstrapper** is a companion utility for Jira Azure DevOps migrator PRO, which is designed to help you with getting started migrating issues from Jira to Azure DevOps as smoothly as possible and with as little friction as possible.

The bootstrapper can do the following:

- Automate user mapping between Jira and Azure DevOps
- Automatically generate the Jira Azure DevOps Migrator configuration file, thus enabling you to get started migrating faster
- Viewing the Jira workflow and assisting with field and state mapping

### Jira Test Management Migrator

**The Jira Test Management migrator (JTMM)** is a powerful tool designed to help you easily migrate your Jira test management data to Azure DevOps Test Plans. With this tool, you can migrate all your test data from Jira to **Azure DevOps Test Plans** without losing any data or compromising the integrity of your test management system, including:

- Test cases
- Test plans
- Test hierarchy and links.

Our tool supports the following Jira test frameworks:

- QMetry
- Zephyr
- Xray
- (More to come soon!)