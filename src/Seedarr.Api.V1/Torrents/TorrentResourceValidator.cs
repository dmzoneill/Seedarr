using FluentValidation;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Torrents;

public class TorrentResourceValidator : ResourceValidator<TorrentResource>
{
    public TorrentResourceValidator()
    {
        RuleFor(t => t.Name)
            .NotEmpty()
            .WithMessage("'Name' must not be empty.");
    }
}
