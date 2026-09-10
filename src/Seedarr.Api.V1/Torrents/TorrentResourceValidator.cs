using FluentValidation;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Torrents;

public class TorrentResourceValidator : ResourceValidator<TorrentResource>
{
    private const string InfoHashPattern = "^[a-fA-F0-9]{40}$";

    public TorrentResourceValidator()
    {
        RuleFor(t => t.Name)
            .NotEmpty()
            .WithMessage("'Name' must not be empty.");
    }
}
