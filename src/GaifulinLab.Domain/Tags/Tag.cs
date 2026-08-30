using GaifulinLab.Domain.Common;

namespace GaifulinLab.Domain.Tags;

public sealed class Tag
{
    private Tag()
    {
    }

    private Tag(string name)
    {
        Id = Guid.NewGuid();
        Rename(name);
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public static Tag Create(string name) => new(name);

    public void Rename(string name)
    {
        Name = DomainRules.RequireTrimmed(name, nameof(name));
        NormalizedName = Name.ToLowerInvariant();
    }
}
