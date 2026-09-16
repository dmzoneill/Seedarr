using System;

namespace NzbDrone.Common.Composition;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ScopedAttribute : Attribute
{
}
