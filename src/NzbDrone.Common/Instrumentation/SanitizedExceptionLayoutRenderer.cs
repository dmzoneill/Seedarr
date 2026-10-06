using System.Text;
using NLog;
using NLog.LayoutRenderers;

namespace NzbDrone.Common.Instrumentation;

[LayoutRenderer("sanitized-exception")]
public sealed class SanitizedExceptionLayoutRenderer : LayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
    {
        if (logEvent.Exception == null)
        {
            return;
        }

        builder.Append(RingBufferTarget.Sanitize(logEvent.Exception.ToString()));
    }
}
