using System.ComponentModel.DataAnnotations;

namespace GaifulinLab.Contracts.Articles;

public sealed class ArticleSearchRequest
{
    [Required, RegularExpression("^[a-z]{2}$")] public string LanguageCode { get; set; } = "en";
    [StringLength(200)] public string? Query { get; set; }
    [Required, RegularExpression("^(all|title|content|topics|tags)$")] public string Scope { get; set; } = "all";
    [StringLength(200)] public string? Topic { get; set; }
    [Required, MaxLength(20)] public string[] Tag { get; set; } = [];
    [Required, RegularExpression("^(all|week|month|year)$")] public string Period { get; set; } = "all";
    [Required, RegularExpression("^(relevance|newest|oldest)$")] public string Sort { get; set; } = "relevance";
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 10;
}
