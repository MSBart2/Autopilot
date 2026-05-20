using Cyberpilot.Pipeline;
using Cyberpilot.Web.Controllers;
using Cyberpilot.Web.Models;
using Cyberpilot.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cyberpilot.Web.UnitTests.Controllers;

public class PipelineAdminControllerTests
{
    // -----------------------------------------------------------------------
    // NewPipeline
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NewPipeline_ReturnsView_WithDefaultStages()
    {
        var controller = CreateController();

        var result = Assert.IsType<ViewResult>(await controller.NewPipeline(default));

        var model = Assert.IsType<PipelineAdminDefinitionEditViewModel>(result.Model);
        Assert.Equal(2, model.Stages.Count);
        Assert.Equal("triage", model.Stages[0].Name);
        Assert.Equal("plan", model.Stages[1].Name);
    }

    // -----------------------------------------------------------------------
    // EditPipeline
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EditPipeline_WithUnknownName_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = await controller.EditPipeline("does-not-exist", default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EditPipeline_WithKnownName_ReturnsPopulatedModel()
    {
        var store = new TestPipelineDefinitionAdminStore();
        var definition = new PipelineAdminDefinition(
            "my-pipeline", "1.0",
            new PipelineAdminDefinitionPolicy("standard", "Standard"),
            [new PipelineAdminStage("Triage", "triage", "triage.md", "sdk/triage", new PipelineAdminStageContract("1.0", []), [])],
            []);
        store.AddDefinition(definition);
        var controller = CreateController(store);

        var result = Assert.IsType<ViewResult>(await controller.EditPipeline("my-pipeline", default));

        var model = Assert.IsType<PipelineAdminDefinitionEditViewModel>(result.Model);
        Assert.Equal("my-pipeline", model.Name);
        Assert.Single(model.Stages);
        Assert.Equal("triage", model.Stages[0].Name);
    }

    // -----------------------------------------------------------------------
    // SavePipeline
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SavePipeline_WithValidModel_RedirectsToIndex()
    {
        var controller = CreateController();
        var model = ValidModel();

        var result = Assert.IsType<RedirectToActionResult>(await controller.SavePipeline(model, default));

        Assert.Equal(nameof(PipelineAdminController.Index), result.ActionName);
    }

    [Fact]
    public async Task SavePipeline_WithInvalidModel_ReturnsPipelineView()
    {
        var controller = CreateController();
        controller.ModelState.AddModelError("Name", "Required");
        var model = new PipelineAdminDefinitionEditViewModel();

        var result = Assert.IsType<ViewResult>(await controller.SavePipeline(model, default));

        Assert.Equal("Pipeline", result.ViewName);
    }

    [Fact]
    public async Task SavePipeline_WithStoreException_AddsModelError()
    {
        var store = new TestPipelineDefinitionAdminStore(throwOnSave: true);
        var controller = CreateController(store);
        var model = ValidModel();

        var result = Assert.IsType<ViewResult>(await controller.SavePipeline(model, default));

        Assert.Equal("Pipeline", result.ViewName);
        Assert.False(controller.ModelState.IsValid);
    }

    // -----------------------------------------------------------------------
    // SavePipeline — WizardActiveStep routing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SavePipeline_SetsWizardActiveStep_WhenBasicsInvalid()
    {
        var controller = CreateController();
        controller.ModelState.AddModelError("Name", "Required");
        var model = new PipelineAdminDefinitionEditViewModel();

        var result = Assert.IsType<ViewResult>(await controller.SavePipeline(model, default));

        var returned = Assert.IsType<PipelineAdminDefinitionEditViewModel>(result.Model);
        Assert.Equal(1, returned.WizardActiveStep);
    }

    [Fact]
    public async Task SavePipeline_SetsWizardActiveStep_WhenStageInvalid()
    {
        var controller = CreateController();
        controller.ModelState.AddModelError("Stages[0].DisplayName", "Required");
        var model = new PipelineAdminDefinitionEditViewModel();

        var result = Assert.IsType<ViewResult>(await controller.SavePipeline(model, default));

        var returned = Assert.IsType<PipelineAdminDefinitionEditViewModel>(result.Model);
        Assert.Equal(2, returned.WizardActiveStep);
    }

    [Fact]
    public async Task SavePipeline_SetsWizardActiveStep_WhenGateInvalid()
    {
        var controller = CreateController();
        controller.ModelState.AddModelError("Stages[0].GatesText", "Invalid format");
        var model = new PipelineAdminDefinitionEditViewModel();

        var result = Assert.IsType<ViewResult>(await controller.SavePipeline(model, default));

        var returned = Assert.IsType<PipelineAdminDefinitionEditViewModel>(result.Model);
        Assert.Equal(3, returned.WizardActiveStep);
    }

    [Fact]
    public async Task SavePipeline_SetsWizardActiveStep_WhenTransitionInvalid()
    {
        var controller = CreateController();
        controller.ModelState.AddModelError("TransitionsText", "Invalid format");
        var model = new PipelineAdminDefinitionEditViewModel();

        var result = Assert.IsType<ViewResult>(await controller.SavePipeline(model, default));

        var returned = Assert.IsType<PipelineAdminDefinitionEditViewModel>(result.Model);
        Assert.Equal(4, returned.WizardActiveStep);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static PipelineAdminController CreateController(TestPipelineDefinitionAdminStore? store = null)
    {
        var adminStore = store ?? new TestPipelineDefinitionAdminStore();
        var controller = new PipelineAdminController(adminStore, NullLogger<PipelineAdminController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        controller.TempData = new TempDataDictionary(
            controller.ControllerContext.HttpContext,
            new TestTempDataProvider());
        return controller;
    }

    private static PipelineAdminDefinitionEditViewModel ValidModel() => new()
    {
        Name = "test-pipeline",
        Version = "1.0",
        PolicyProfileName = "standard",
        Stages =
        [
            new() { DisplayName = "Triage", Name = "triage", PromptFile = "triage.md", Label = "sdk/triage", ContractVersion = "1.0" },
        ],
    };

    // -----------------------------------------------------------------------
    // Test doubles
    // -----------------------------------------------------------------------

    private sealed class TestPipelineDefinitionAdminStore : IPipelineDefinitionAdminStore
    {
        private readonly bool throwOnSave;
        private readonly List<PipelineAdminDefinition> definitions = [];

        public TestPipelineDefinitionAdminStore(bool throwOnSave = false)
        {
            this.throwOnSave = throwOnSave;
        }

        public void AddDefinition(PipelineAdminDefinition definition) => definitions.Add(definition);

        public string DefinitionFilePath => Path.Combine(Path.GetTempPath(), "pipeline-admin-test.json");

        public Task<PipelineDefinitionAdminFile> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PipelineDefinitionAdminFile(definitions, []));

        public Task SaveDefinitionAsync(PipelineAdminDefinitionEditViewModel model, CancellationToken cancellationToken = default)
        {
            if (throwOnSave) throw new InvalidOperationException("Save failed (test).");
            return Task.CompletedTask;
        }

        public Task DeleteDefinitionAsync(string name, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SavePolicyAsync(PipelineAdminPolicyEditViewModel model, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeletePolicyAsync(string name, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<PipelineDefinitionOptionViewModel>> GetDefinitionOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PipelineDefinitionOptionViewModel>>([]);

        public Task<IReadOnlyList<PipelinePolicyOptionViewModel>> GetPolicyOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PipelinePolicyOptionViewModel>>([]);

        public Task<PipelineAdminDefinition?> FindDefinitionAsync(string name, CancellationToken cancellationToken = default)
        {
            var def = definitions.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult<PipelineAdminDefinition?>(def);
        }

        public Task<PipelineAdminPolicyProfile?> FindPolicyAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<PipelineAdminPolicyProfile?>(null);
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        private Dictionary<string, object?> _data = [];
        public IDictionary<string, object?> LoadTempData(HttpContext context) => _data;
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) => _data = new Dictionary<string, object?>(values);
    }
}
