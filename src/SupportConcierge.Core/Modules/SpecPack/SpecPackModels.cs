namespace SupportConcierge.Core.Modules.SpecPack;

public sealed class SpecPackConfig
{
    public List<Category> Categories { get; set; } = new();
    public Dictionary<string, CategoryChecklist> Checklists { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ValidatorRules Validators { get; set; } = new();
    public RoutingRules Routing { get; set; } = new();
    public Dictionary<string, string> Playbooks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class Category
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = new();
}

public sealed class CategoryChecklist
{
    public string Category { get; set; } = string.Empty;
    public int CompletenessThreshold { get; set; } = 70;
    public List<RequiredField> RequiredFields { get; set; } = new();
}

public sealed class RequiredField
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Weight { get; set; } = 10;
    public bool Optional { get; set; }
    public List<string> Aliases { get; set; } = new();
}

public sealed class ValidatorRules
{
    public List<string> JunkPatterns { get; set; } = new();
    public Dictionary<string, string> FormatValidators { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> SecretPatterns { get; set; } = new();
    public List<ContradictionRule> ContradictionRules { get; set; } = new();
}

public sealed class ContradictionRule
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Field1 { get; set; } = string.Empty;
    public string Field2 { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
}

public sealed class RoutingRules
{
    public List<CategoryRoute> Routes { get; set; } = new();
    public List<string> EscalationMentions { get; set; } = new();
}

public sealed class CategoryRoute
{
    public string Category { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = new();
    public List<string> Assignees { get; set; } = new();
}

