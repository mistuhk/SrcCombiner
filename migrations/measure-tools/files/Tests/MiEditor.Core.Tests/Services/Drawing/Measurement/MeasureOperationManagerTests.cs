using WG.MiEditor.Core.Services.Drawing.Measurement.Context;
using WG.MiEditor.Core.Services.Drawing.Measurement.Manager;
using WG.MiEditor.Core.Services.Drawing.Measurement.Operation;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Measurement;

public class MeasureOperationManagerTests
{
    // The manager only passes the context through to the operation, so the fakes never touch
    // the map view or overlay and a live MAUI MapView is not needed here.
    private static readonly MeasurementContext Context = new(null!, null!);

    private readonly List<string> log = [];
    private readonly FakeContextProvider provider;
    private readonly FakeOperation distance;
    private readonly FakeOperation area;
    private readonly MeasureOperationManager sut;

    public MeasureOperationManagerTests()
    {
        provider = new FakeContextProvider(log);
        distance = new FakeOperation(MeasureTool.Distance, log);
        area = new FakeOperation(MeasureTool.Area, log);
        sut = new MeasureOperationManager(provider, [distance, area]);
    }

    [Fact]
    public async Task Activate_StartsTheOperationWithAFreshContext()
    {
        await sut.Activate(MeasureTool.Distance);

        Assert.Equal(1, distance.StartCount);
        Assert.Same(Context, distance.LastContext);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task Activate_SameToolTwice_StartsItOnce()
    {
        await sut.Activate(MeasureTool.Distance);
        await sut.Activate(MeasureTool.Distance);

        Assert.Equal(1, distance.StartCount);
        Assert.Equal(0, distance.StopCount);
    }

    [Fact]
    public async Task Activate_AnotherTool_StopsThePreviousBeforeStartingTheNext()
    {
        await sut.Activate(MeasureTool.Distance);
        log.Clear();

        await sut.Activate(MeasureTool.Area);

        Assert.Equal(["stop Distance", "context", "start Area"], log);
    }

    [Fact]
    public async Task Activate_RequestsContextEveryTime_SoAnOverlayRemovedElsewhereIsReattached()
    {
        await sut.Activate(MeasureTool.Distance);
        await sut.Deactivate();
        await sut.Activate(MeasureTool.Distance);

        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task Activate_WhenMapNotReady_Throws_AndLeavesNothingActive()
    {
        provider.Failure = new MeasurementUnavailableException("The map is not ready yet.");

        await Assert.ThrowsAsync<MeasurementUnavailableException>(() => sut.Activate(MeasureTool.Distance));

        Assert.Equal(0, distance.StartCount);

        await sut.Deactivate();
        Assert.Equal(0, distance.StopCount);
    }

    [Fact]
    public async Task Activate_AfterMapBecomesReady_Succeeds()
    {
        provider.Failure = new MeasurementUnavailableException("The map is not ready yet.");
        await Assert.ThrowsAsync<MeasurementUnavailableException>(() => sut.Activate(MeasureTool.Distance));

        provider.Failure = null;
        await sut.Activate(MeasureTool.Distance);

        Assert.Equal(1, distance.StartCount);
    }

    [Fact]
    public async Task Activate_WhenStartThrows_DoesNotTreatTheOperationAsCurrent()
    {
        distance.StartFailure = new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Activate(MeasureTool.Distance));

        distance.StartFailure = null;
        await sut.Activate(MeasureTool.Distance);

        Assert.Equal(2, distance.StartCount);
    }

    [Fact]
    public async Task Deactivate_WhenStopThrows_StillClearsTheCurrentOperation()
    {
        await sut.Activate(MeasureTool.Distance);
        distance.StopFailure = new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Deactivate());

        // The failed operation was already forgotten, so a second deactivate does not retry it.
        distance.StopFailure = null;
        await sut.Deactivate();
        Assert.Equal(0, distance.StopCount);

        // The manager is not wedged: activating again starts afresh.
        await sut.Activate(MeasureTool.Distance);
        Assert.Equal(2, distance.StartCount);
    }

    [Fact]
    public async Task Deactivate_WhenNothingActive_IsANoOp()
    {
        await sut.Deactivate();

        Assert.Equal(0, distance.StopCount);
        Assert.Equal(0, area.StopCount);
    }

    [Fact]
    public async Task ConcurrentActivations_AreSerialised()
    {
        var gate = new TaskCompletionSource();
        provider.Gate = gate.Task;

        var first = sut.Activate(MeasureTool.Distance);
        var second = sut.Activate(MeasureTool.Area);

        // The second activation must not reach the provider while the first is still inside it.
        Assert.Equal(1, provider.CallCount);

        provider.Gate = null;
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, provider.MaxConcurrentCalls);
        Assert.Equal(["context", "start Distance", "stop Distance", "context", "start Area"], log);
    }

    [Fact]
    public async Task Activate_UnknownTool_Throws()
    {
        var manager = new MeasureOperationManager(provider, [distance]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => manager.Activate(MeasureTool.Area));
    }

    [Fact]
    public void Constructor_DuplicateOperationsForATool_Throws()
    {
        var duplicate = new FakeOperation(MeasureTool.Distance, log);

        Assert.Throws<ArgumentException>(() => new MeasureOperationManager(provider, [distance, duplicate]));
    }

    private sealed class FakeContextProvider(List<string> log) : IMeasurementContextProvider
    {
        private int inFlight;

        public int CallCount { get; private set; }

        public int MaxConcurrentCalls { get; private set; }

        public Exception? Failure { get; set; }

        public Task? Gate { get; set; }

        public async Task<MeasurementContext> GetAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            inFlight++;
            MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, inFlight);

            try
            {
                if (Gate is { } gate) await gate;
                if (Failure is { } failure) throw failure;

                log.Add("context");
                return Context;
            }
            finally
            {
                inFlight--;
            }
        }
    }

    private sealed class FakeOperation(MeasureTool tool, List<string> log) : IMeasureOperation
    {
        public MeasureTool Tool { get; } = tool;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public MeasurementContext? LastContext { get; private set; }

        public Exception? StartFailure { get; set; }

        public Exception? StopFailure { get; set; }

        public void Start(MeasurementContext context)
        {
            StartCount++;
            if (StartFailure is { } failure) throw failure;

            LastContext = context;
            log.Add($"start {Tool}");
        }

        public void Stop()
        {
            if (StopFailure is { } failure) throw failure;

            StopCount++;
            log.Add($"stop {Tool}");
        }
    }
}
