using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Application.Documents.Services
{
    public sealed class DocumentFolderDecisionEngine : IDocumentFolderDecisionEngine
    {
        private readonly AppDbContext _dbContext;

        public DocumentFolderDecisionEngine(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<DocumentFolderDecisionResultDto> DecideAsync(
            Document document,
            DocumentFolderAnalysisResultDto analysis,
            IReadOnlyCollection<DocumentFolder> existingFolders,
            CancellationToken cancellationToken)
        {
            if (document.FolderId is not null)
            {
                return new DocumentFolderDecisionResultDto
                {
                    FolderId = document.FolderId,
                    CreatedNewFolder = false,
                    AutoAssigned = false,
                    Status = "manual",
                    Confidence = 1m,
                    Reason = "Folder selected manually during upload."
                };
            }

            if (document.OrganizationMode == DocumentOrganizationMode.Disabled)
            {
                return new DocumentFolderDecisionResultDto
                {
                    Status = "disabled",
                    Confidence = null,
                    Reason = "Smart organization is disabled for this document."
                };
            }

            var bestExisting = analysis.ExistingFolderCandidates
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            var assignExistingThreshold = analysis.Category switch
            {
                "invoices" => 0.55m,
                "cv" => 0.55m,
                "contracts" => 0.58m,
                "documentation" => 0.60m,
                _ => 0.62m
            };

            var createThreshold = analysis.Category switch
            {
                "invoices" => 0.72m,
                "cv" => 0.72m,
                "contracts" => 0.75m,
                "documentation" => 0.78m,
                _ => 0.86m
            };


            if (bestExisting is not null && bestExisting.Score >= assignExistingThreshold)
            {
                return new DocumentFolderDecisionResultDto
                {
                    FolderId = bestExisting.FolderId,
                    CreatedNewFolder = false,
                    AutoAssigned = true,
                    Status = "auto-assigned",
                    Confidence = bestExisting.Score,
                    Reason = bestExisting.Reason
                };
            }

            if (document.OrganizationMode == DocumentOrganizationMode.AutoAssignExistingOnly)
            {
                return new DocumentFolderDecisionResultDto
                {
                    FolderId = null,
                    CreatedNewFolder = false,
                    AutoAssigned = false,
                    Status = "suggested",
                    Confidence = analysis.Confidence,
                    Reason = analysis.Reason
                };
            }

            if (document.OrganizationMode == DocumentOrganizationMode.AutoAssignOrCreate &&
                analysis.ProposedFolder is not null &&
                analysis.Confidence >= createThreshold)
            {
                var createdOrExisting = await GetOrCreateFolderAsync(
                    document.UserId,
                    analysis.ProposedFolder,
                    cancellationToken);

                return new DocumentFolderDecisionResultDto
                {
                    FolderId = createdOrExisting.Id,
                    CreatedNewFolder = createdOrExisting.IsSystemGenerated,
                    AutoAssigned = true,
                    Status = "auto-created-and-assigned",
                    Confidence = analysis.Confidence,
                    Reason = analysis.Reason
                };
            }

            return new DocumentFolderDecisionResultDto
            {
                FolderId = null,
                CreatedNewFolder = false,
                AutoAssigned = false,
                Status = "suggested",
                Confidence = analysis.Confidence,
                Reason = analysis.Reason
            };
        }

        private async Task<DocumentFolder> GetOrCreateFolderAsync(
            Guid userId,
            DocumentFolderProposalDto proposal,
            CancellationToken cancellationToken)
        {
            var existing = await _dbContext.DocumentFolders.FirstOrDefaultAsync(
                x => x.UserId == userId &&
                     x.ParentFolderId == proposal.ParentFolderId &&
                     x.Key == proposal.Key,
                cancellationToken);

            if (existing is not null)
            {
                existing.IsSystemGenerated = existing.IsSystemGenerated || true;
                return existing;
            }

            var folder = new DocumentFolder
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ParentFolderId = proposal.ParentFolderId,
                Key = proposal.Key,
                Name = proposal.Name,
                NamePl = proposal.NamePl,
                NameEn = proposal.NameEn,
                NameUa = proposal.NameUa,
                IsSystemGenerated = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.DocumentFolders.Add(folder);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return folder;
        }
    }
}