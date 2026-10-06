using System.Text;
using NLog;
using NLog.LayoutRenderers;

namespace NzbDrone.Common.Instrumentation;

[LayoutRenderer("sanitized-message")]
public sealed class SanitizedMessageLayoutRenderer : LayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
    {
        builder.Append(RingBufferTarget.Sanitize(logEvent.FormattedMessage));
    }
}
