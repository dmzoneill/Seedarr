using System;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Pieces;

public class SequentialPiecePicker : Peers.PiecePicker.SequentialPiecePicker, IPiecePicker
{
    public SequentialPiecePicker(Peers.PiecePicker.IPiecePicker rarestFirstPicker = null, IRandomNumberGenerator random = null)
        : base(rarestFirstPicker, random)
    {
    }
}

public class SequentialPicker : SequentialPiecePicker
{
    public SequentialPicker(Peers.PiecePicker.IPiecePicker rarestFirstPicker = null, IRandomNumberGenerator random = null)
        : base(rarestFirstPicker, random)
    {
    }
}
