using Moq;
using WG.MiEditor.Core.Services.Drawing.Measurement.Components;
using WG.MiEditor.Core.Services.Drawing.Measurement.Context;
using WG.MiEditor.Core.Services.Notifications;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Shared.Models.Notifications;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Measurement;

public class MeasurementErrorReporterTests
{
    private readonly Mock<ILoggerService> logger = new();
    private readonly Mock<INotificationManager> notifications = new();
    private readonly MeasurementErrorReporter sut;

    public MeasurementErrorReporterTests() => sut = new MeasurementErrorReporter(logger.Object, notifications.Object);

    [Fact]
    public void Report_LogsTheException_AndShowsAGenericMessage()
    {
        var ex = new InvalidOperationException("internal detail");

        sut.Report("Measurement", ex);

        logger.Verify(l => l.Error(ex, It.IsAny<string>()), Times.Once);
        notifications.Verify(n => n.Show("Measurement", MeasurementErrorReporter.GenericMessage, NotificationType.Error), Times.Once);
    }

    [Fact]
    public void Report_ShowsTheUserFacingMessage_ForUnavailableMap()
    {
        sut.Report("Measurement", new MeasurementUnavailableException("The map is not ready yet."));

        notifications.Verify(n => n.Show("Measurement", "The map is not ready yet.", NotificationType.Error), Times.Once);
    }

    [Fact]
    public void Report_NeverThrows_EvenIfLoggingAndNotifyingFail()
    {
        logger.Setup(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>())).Throws<InvalidOperationException>();
        notifications.Setup(n => n.Show(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NotificationType>())).Throws<InvalidOperationException>();

        var thrown = Record.Exception(() => sut.Report("Measurement", new Exception()));

        Assert.Null(thrown);
    }
}
