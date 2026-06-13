using FluentAssertions;
using MAIS.Core.Events;
using MAIS.Core.Models;
using MAIS.Server.Service.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MAIS.Integration.Tests;

public sealed class ModuleRegistryTests
{
    private static ModuleRegistry CreateRegistry() =>
        new(NullLogger<ModuleRegistry>.Instance);

    // ── Registration ──────────────────────────────────────────────────────

    [Fact]
    public void Register_NewModule_AddsToRegistry()
    {
        var registry = CreateRegistry();
        var module = BuildModule("test.module");

        registry.Register(module);

        registry.GetAll().Should().ContainSingle(d => d.Id == "test.module");
    }

    [Fact]
    public void Register_DuplicateId_Throws()
    {
        var registry = CreateRegistry();
        var module = BuildModule("test.module");

        registry.Register(module);

        var act = () => registry.Register(module);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*test.module*");
    }

    [Fact]
    public void Unregister_ExistingModule_RemovesFromRegistry()
    {
        var registry = CreateRegistry();
        registry.Register(BuildModule("test.module"));

        registry.Unregister("test.module");

        registry.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Get_ExistingModule_ReturnsInstance()
    {
        var registry = CreateRegistry();
        var module = BuildModule("test.module");
        registry.Register(module);

        var result = registry.Get("test.module");

        result.Should().BeSameAs(module);
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull()
    {
        var registry = CreateRegistry();

        registry.Get("nonexistent").Should().BeNull();
    }

    // ── Status updates ────────────────────────────────────────────────────

    [Fact]
    public void UpdateStatus_ChangesDescriptorStatus()
    {
        var registry = CreateRegistry();
        registry.Register(BuildModule("test.module"));

        registry.UpdateStatus("test.module", ModuleStatus.Running);

        registry.GetAll().Single().Status.Should().Be(ModuleStatus.Running);
    }

    [Fact]
    public void UpdateStatus_RaisesEvent_WhenStatusChanges()
    {
        var registry = CreateRegistry();
        registry.Register(BuildModule("test.module"));

        ModuleStatusChangedEventArgs? captured = null;
        registry.ModuleStatusChanged += (_, args) => captured = args;

        registry.UpdateStatus("test.module", ModuleStatus.Running);

        captured.Should().NotBeNull();
        captured!.ModuleId.Should().Be("test.module");
        captured.PreviousStatus.Should().Be(ModuleStatus.Unknown);
        captured.NewStatus.Should().Be(ModuleStatus.Running);
    }

    [Fact]
    public void UpdateStatus_DoesNotRaiseEvent_WhenStatusUnchanged()
    {
        var registry = CreateRegistry();
        registry.Register(BuildModule("test.module"));
        registry.UpdateStatus("test.module", ModuleStatus.Running);

        var eventCount = 0;
        registry.ModuleStatusChanged += (_, _) => eventCount++;

        // Same status — should not re-fire
        registry.UpdateStatus("test.module", ModuleStatus.Running);

        eventCount.Should().Be(0);
    }

    // ── Thread safety ─────────────────────────────────────────────────────

    [Fact]
    public async Task Register_ConcurrentCalls_AllModulesRegistered()
    {
        var registry = CreateRegistry();
        var ids = Enumerable.Range(0, 50).Select(i => $"module.{i}").ToList();

        await Parallel.ForEachAsync(ids, async (id, _) =>
        {
            await Task.Yield(); // Force scheduling across threads
            registry.Register(BuildModule(id));
        });

        registry.GetAll().Should().HaveCount(50);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static MAIS.Core.Abstractions.IModule BuildModule(string id)
    {
        var module = Substitute.For<MAIS.Core.Abstractions.IModule>();
        module.Id.Returns(id);
        module.DisplayName.Returns($"Module {id}");
        module.Description.Returns("Test module");
        module.Version.Returns("1.0.0");
        module.Type.Returns(ModuleType.InProcess);
        module.LaunchUri.Returns((Uri?)null);
        return module;
    }
}
