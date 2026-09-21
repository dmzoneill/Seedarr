using System;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Pieces;

public class RarestFirstPiecePicker : Peers.PiecePicker.RarestFirstPiecePicker, IPiecePicker
{
    public RarestFirstPiecePicker(IRandomNumberGenerator random = null)
        : base(random)
    {
    }
}

public class RarestFirstPicker : RarestFirstPiecePicker
{
    public RarestFirstPicker(IRandomNumberGenerator random = null)
        : base(random)
    {
    }
}
