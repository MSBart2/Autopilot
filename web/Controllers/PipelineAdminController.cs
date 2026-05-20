using Cyberpilot.Web.Models;
using Cyberpilot.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cyberpilot.Web.Controllers;

/// <summary>
/// Displays and edits operator-managed pipeline definitions and policy profiles.
/// </summary>
[Route("[controller]")]
public sealed class PipelineAdminController(IPipelineDefinitionAdminStore store, ILogger<PipelineAdminController> logger) : Controller
{
    /// <summary>
    /// Displays the editable pipeline configuration dashboard.
    /// </summary>
    /// <returns>The admin index view.</returns>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var file = await store.ReadAsync(cancellationToken);
        var model = new PipelineAdminIndexViewModel(
            file.Definitions.Select(definition => new PipelineAdminDefinitionSummaryViewModel(
                definition.Name,
                definition.Version,
                definition.PolicyProfile.Name,
                definition.Stages.Count)).ToArray(),
            file.PolicyProfiles.Select(profile => new PipelineAdminPolicySummaryViewModel(
                profile.Name,
                profile.Strictness,
                profile.Description)).ToArray(),
            store.DefinitionFilePath);

        return View(model);
    }

    /// <summary>
    /// Displays a form for creating a pipeline definition.
    /// </summary>
    /// <returns>The pipeline definition editor view.</returns>
    [HttpGet("Pipelines/New")]
    public async Task<IActionResult> NewPipeline(CancellationToken cancellationToken)
    {
        return View("Pipeline", await PopulatePoliciesAsync(new PipelineAdminDefinitionEditViewModel
        {
            Stages =
            [
                new() { DisplayName = "Triage", Name = "triage", PromptFile = "triage.prompt.md", Label = "sdk/triage" },
                new() { DisplayName = "Plan", Name = "plan", PromptFile = "plan.prompt.md", Label = "sdk/plan" },
            ],
        }, cancellationToken));
    }

    /// <summary>
    /// Displays a form for editing a pipeline definition.
    /// </summary>
    /// <param name="name">The pipeline definition name.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The pipeline definition editor view.</returns>
    [HttpGet("Pipelines/{name}")]
    public async Task<IActionResult> EditPipeline(string name, CancellationToken cancellationToken)
    {
        var definition = await store.FindDefinitionAsync(name, cancellationToken);
        if (definition is null)
        {
            return NotFound();
        }

        var model = new PipelineAdminDefinitionEditViewModel
        {
            OriginalName = definition.Name,
            Name = definition.Name,
            Version = definition.Version,
            PolicyProfileName = definition.PolicyProfile.Name,
            Stages = definition.Stages.Select(stage => new PipelineAdminStageInputModel
            {
                DisplayName = stage.DisplayName,
                Name = stage.Name,
                PromptFile = stage.PromptFile,
                Label = stage.Label,
                ContractVersion = stage.Contract.Version,
                RequiredArtifactsText = string.Join(", ", stage.Contract.RequiredArtifacts),
                GatesText = string.Join('\n', stage.Gates.Select(gate => $"{gate.Name}|{gate.Timing}|{gate.IsBlocking}")),
            }).ToList(),
            TransitionsText = string.Join('\n', definition.Transitions.Select(transition => $"{transition.FromStage}|{transition.ToStage}|{transition.Condition}")),
        };

        return View("Pipeline", await PopulatePoliciesAsync(model, cancellationToken));
    }

    /// <summary>
    /// Saves a pipeline definition from the editor form.
    /// </summary>
    /// <param name="model">The submitted pipeline definition values.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A redirect to the admin index when saved, or the editor view when validation fails.</returns>
    [HttpPost("Pipelines")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePipeline(PipelineAdminDefinitionEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.WizardActiveStep = ComputeWizardStep(ModelState);
            return View("Pipeline", await PopulatePoliciesAsync(model, cancellationToken));
        }

        try
        {
            await store.SaveDefinitionAsync(model, cancellationToken);
            TempData["PipelineAdminNotice"] = $"Pipeline '{model.Name}' saved.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(ex, "Pipeline definition save failed for {PipelineName}.", model.Name);
            ModelState.AddModelError(string.Empty, ex.Message);
            model.WizardActiveStep = ComputeWizardStep(ModelState);
            return View("Pipeline", await PopulatePoliciesAsync(model, cancellationToken));
        }
    }

    /// <summary>
    /// Deletes an editable pipeline definition.
    /// </summary>
    /// <param name="name">The pipeline definition name.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A redirect to the admin index.</returns>
    [HttpPost("Pipelines/{name}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePipeline(string name, CancellationToken cancellationToken)
    {
        await store.DeleteDefinitionAsync(name, cancellationToken);
        TempData["PipelineAdminNotice"] = $"Pipeline '{name}' deleted.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Displays a form for creating a policy profile.
    /// </summary>
    /// <returns>The policy profile editor view.</returns>
    [HttpGet("Policies/New")]
    public IActionResult NewPolicy()
    {
        return View("Policy", new PipelineAdminPolicyEditViewModel());
    }

    /// <summary>
    /// Displays a form for editing a policy profile.
    /// </summary>
    /// <param name="name">The policy profile name.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The policy profile editor view.</returns>
    [HttpGet("Policies/{name}")]
    public async Task<IActionResult> EditPolicy(string name, CancellationToken cancellationToken)
    {
        var policy = await store.FindPolicyAsync(name, cancellationToken);
        if (policy is null)
        {
            return NotFound();
        }

        return View("Policy", new PipelineAdminPolicyEditViewModel
        {
            OriginalName = policy.Name,
            Name = policy.Name,
            Strictness = policy.Strictness,
            Description = policy.Description,
        });
    }

    /// <summary>
    /// Saves a policy profile from the editor form.
    /// </summary>
    /// <param name="model">The submitted policy values.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A redirect to the admin index when saved, or the editor view when validation fails.</returns>
    [HttpPost("Policies")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePolicy(PipelineAdminPolicyEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Policy", model);
        }

        await store.SavePolicyAsync(model, cancellationToken);
        TempData["PipelineAdminNotice"] = $"Policy '{model.Name}' saved.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Deletes an editable policy profile.
    /// </summary>
    /// <param name="name">The policy profile name.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A redirect to the admin index.</returns>
    [HttpPost("Policies/{name}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePolicy(string name, CancellationToken cancellationToken)
    {
        await store.DeletePolicyAsync(name, cancellationToken);
        TempData["PipelineAdminNotice"] = $"Policy '{name}' deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<PipelineAdminDefinitionEditViewModel> PopulatePoliciesAsync(PipelineAdminDefinitionEditViewModel model, CancellationToken cancellationToken)
    {
        var file = await store.ReadAsync(cancellationToken);
        model.PolicyProfiles = file.PolicyProfiles
            .Select(profile => new PipelineAdminPolicySummaryViewModel(profile.Name, profile.Strictness, profile.Description))
            .DefaultIfEmpty(new PipelineAdminPolicySummaryViewModel("standard", "Standard", "Balanced default gates and diagnostics"))
            .ToArray();
        return model;
    }

    /// <summary>
    /// Returns the lowest 1-based wizard step number that has a ModelState validation error.
    /// Step 1 = Basics (Name, Version, PolicyProfileName).
    /// Step 2 = Stages (Stages[*] non-gate fields).
    /// Step 3 = Gates (Stages[*].GatesText).
    /// Step 4 = Transitions (TransitionsText).
    /// Falls back to step 1 for model-level or unrecognised keys.
    /// </summary>
    private static int ComputeWizardStep(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState)
    {
        static bool HasError(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary ms, string key)
            => ms.ContainsKey(key) && ms[key]!.Errors.Count > 0;

        static bool HasErrorPrefix(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary ms, string prefix)
            => ms.Keys.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && ms[k]!.Errors.Count > 0);

        // Step 1 — Basics
        if (HasError(modelState, nameof(Models.PipelineAdminDefinitionEditViewModel.Name))
            || HasError(modelState, nameof(Models.PipelineAdminDefinitionEditViewModel.Version))
            || HasError(modelState, nameof(Models.PipelineAdminDefinitionEditViewModel.PolicyProfileName)))
        {
            return 1;
        }

        // Step 2 — Stages (non-gate fields)
        var stageKeys = modelState.Keys
            .Where(k => k.StartsWith("Stages[", StringComparison.OrdinalIgnoreCase)
                        && !k.EndsWith(".GatesText", StringComparison.OrdinalIgnoreCase)
                        && modelState[k]!.Errors.Count > 0)
            .ToList();
        if (stageKeys.Count > 0)
        {
            return 2;
        }

        // Step 3 — Gates
        if (HasErrorPrefix(modelState, "Stages[") &&
            modelState.Keys.Any(k => k.EndsWith(".GatesText", StringComparison.OrdinalIgnoreCase)
                                     && modelState[k]!.Errors.Count > 0))
        {
            return 3;
        }

        // Step 4 — Transitions
        if (HasError(modelState, nameof(Models.PipelineAdminDefinitionEditViewModel.TransitionsText)))
        {
            return 4;
        }

        // Default: go back to basics (e.g., model-level errors like missing stage)
        return 1;
    }
}
