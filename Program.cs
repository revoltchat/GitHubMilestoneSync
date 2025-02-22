﻿using System;
using Console = Log73.Console;
using System.IO;
using System.Linq;
using System.Text.Json;
using Log73;
using Log73.ExtensionMethod;
using Log73.Extensions;
using Octokit;

// configure logging
Console.Configure.UseNewtonsoftJson();
Console.Options.UseAnsi = false;
Console.Options.LogLevel = LogLevel.Debug;

var github = new GitHubClient(new ProductHeaderValue("GitHubMilestoneSync", "v1.1"))
{
    Credentials = new Credentials(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
};

foreach (var config in JsonSerializer.Deserialize<Config[]>(
             await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("CONFIG_FILE") ?? "config.json"),
             new JsonSerializerOptions
             {
                 PropertyNameCaseInsensitive = true
             })!)
{
    foreach (var repoName in config.Repositories)
    {
        var repo = await github.Repository.Get(repoName.Split('/')[0], repoName.Split('/')[1]);
        $"Milestones for {repoName}".Dump();
        var milestones = await github.Issue.Milestone.GetAllForRepository(repo.Id, new MilestoneRequest()
        {
            // Only gives us Open by default
            State = ItemStateFilter.All
        });
        if (!config.ExcludeFromMilestoneSync?.Contains(repoName) ?? true)
        {
            foreach (var mile in config.Milestones)
            {
                var match = milestones.FirstOrDefault(m => m.Title == mile.Title);
                if (match != null)
                {
                    "Has".DumpDebug();
                    if (match.State.StringValue != mile.State ||
                        match.Description != mile.Description)
                    {
                        "Differs, updating.".Dump();
                        await github.Issue.Milestone.Update(repo.Id, match.Number, new MilestoneUpdate()
                        {
                            Description = mile.Description,
                            State = StateFromString(mile.State)
                        });
                    }
                }
                else
                {
                    "Doesn't have, creating.".DumpDebug();
                    await github.Issue.Milestone.Create(repo.Id, new NewMilestone(mile.Title)
                    {
                        Description = mile.Description,
                        State = StateFromString(mile.State)
                    });
                }
            }

            if (config.DeleteUnknownMilestones ?? false)
            {
                foreach (var mile in milestones)
                {
                    var match = config.Milestones.FirstOrDefault(m => m.Title == mile.Title);
                    if (match == null)
                    {
                        if (config.ExcludeMilestoneDeletion?.Contains(mile.Title) ?? false)
                        {
                            "Milestone is excluded from deletion".DumpDebug();
                        }
                        else
                        {
                            "Doesn't exist anymore, deleting.".DumpDebug();
                            mile.Title.DumpDebug();
                            await github.Issue.Milestone.Delete(repo.Id, mile.Number);
                        }
                    }
                }
            }
        }
        else
        {
            $"Repo {repoName} is excluded from milestone sync.".DumpDebug();
        }

        $"Labels for {repoName}".Dump();
        var labels = await github.Issue.Labels.GetAllForRepository(repo.Id);
        if (!config.ExcludeFromLabelSync?.Contains(repoName) ?? true)
        {
            foreach (var label in config.Labels)
            {
                if (label.Repos?.Length > 0 && !label.Repos.Contains(repoName))
                    continue;

                var match = labels.FirstOrDefault(l => l.Name == label.Name);
                if (match != null)
                {
                    "Has".DumpDebug();
                    if (match.Color != label.Color ||
                        match.Description != label.Description)
                    {
                        "Differs, updating.".DumpDebug();
                        await github.Issue.Labels.Update(repo.Id, label.Name, new LabelUpdate(label.Name, label.Color)
                        {
                            Description = label.Description
                        });
                    }
                }
                else
                {
                    "Doesn't have, creating.".DumpDebug();
                    label.Name.DumpDebug();
                    await github.Issue.Labels.Create(repo.Id, new NewLabel(label.Name, label.Color)
                    {
                        Description = label.Description
                    });
                }
            }

            if (config.DeleteUnknownLabels ?? false)
            {
                foreach (var label in labels)
                {
                    var match = config.Labels.FirstOrDefault(l => l.Name == label.Name);
                    if (match == null)
                    {
                        if (config.ExcludeLabelDeletion?.Contains(label.Name) ?? false)
                        {
                            "Label is excluded from deletion".DumpDebug();
                        }
                        else
                        {
                            "Doesn't exist anymore, deleting.".DumpDebug();
                            label.Name.DumpDebug();
                            await github.Issue.Labels.Delete(repo.Id, label.Name);
                        }
                    }
                }
            }
        }
        else
        {
            $"Repo {repoName} is excluded from label sync.".DumpDebug();
        }
    }
}

"Finished.".Dump();
var rateLimit = github.GetLastApiInfo().RateLimit;
$"Remaining {rateLimit.Remaining} of {rateLimit.Limit} rate limit.".Dump();

static ItemState StateFromString(string str)
    => str.ToLower() switch
    {
        "open" => ItemState.Open,
        "closed" => ItemState.Closed,
        _ => throw new Exception("Invalid state!")
    };

class Config
{
    public string[] Repositories { get; set; }
    public string[]? ExcludeFromLabelSync { get; set; }
    public string[]? ExcludeFromMilestoneSync { get; set; }
    public string[]? ExcludeMilestoneDeletion { get; set; }
    public string[]? ExcludeLabelDeletion { get; set; }
    public bool? DeleteUnknownMilestones { get; set; }
    public bool? DeleteUnknownLabels { get; set; }
    public Milestone[] Milestones { get; set; }
    public Label[] Labels { get; set; }
}

class Milestone
{
    public string Title { get; set; }
    public string State { get; set; }
    public string Description { get; set; }
    public string[]? Repos { get; set; }
}

class Label
{
    public string Name { get; set; }
    public string Color { get; set; }
    public string Description { get; set; }
    public string[]? Repos { get; set; }
}