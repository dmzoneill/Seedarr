using FluentValidation;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Tags;

public class TagResourceValidator : ResourceValidator<TagResource>
{
    private const int MaxLabelLength = 50;

    public TagResourceValidator()
    {
        RuleFor(t => t.Label)
            .NotEmpty()
            .WithMessage("'Label' must not be empty.")
            .MaximumLength(MaxLabelLength)
            .WithMessage($"'Label' must not exceed {MaxLabelLength} characters.");
    }
}
