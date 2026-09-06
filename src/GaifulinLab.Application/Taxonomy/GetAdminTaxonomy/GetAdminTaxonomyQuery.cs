using GaifulinLab.Contracts.Taxonomy;
using MediatR;

namespace GaifulinLab.Application.Taxonomy.GetAdminTaxonomy;

public sealed record GetAdminTaxonomyQuery(string UserId) : IRequest<AdminTaxonomyDto>;
