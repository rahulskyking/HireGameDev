using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Studios;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Constants;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

public sealed class StudioTeamService : IStudioTeamService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditLogger _auditLogger;
    private readonly IClock _clock;

    public StudioTeamService(
        AppDbContext dbContext,
        IAuditLogger auditLogger,
        IClock clock)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    public async Task<Result<StudioTeamState>> GetTeamAsync(
        Guid actorUserId,
        Guid studioId,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty || studioId == Guid.Empty)
        {
            return Result.Failure<StudioTeamState>(
                Error.Validation("StudioTeam.InvalidIdentity", "Valid user and studio identifiers are required."));
        }

        var studio = await _dbContext.StudioProfiles
            .AsNoTracking()
            .Where(s => s.Id == studioId && s.DeletedAt == null)
            .Select(s => new { s.Id, s.Name, s.Slug, s.OwnerUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (studio is null)
        {
            return Result.Failure<StudioTeamState>(
                Error.NotFound("StudioProfile.NotFound", "Studio profile was not found."));
        }

        var callerMember = await _dbContext.StudioMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.StudioId == studio.Id && m.UserId == actorUserId,
                cancellationToken);

        if (callerMember is null)
        {
            return Result.Failure<StudioTeamState>(
                Error.Forbidden("StudioTeam.Forbidden", "You are not a member of this studio workspace."));
        }

        return await BuildTeamStateAsync(
            studio.Id,
            studio.Name,
            studio.Slug,
            studio.OwnerUserId,
            callerMember.MemberRole,
            cancellationToken);
    }

    public async Task<Result<StudioTeamState>> AddMemberByEmailAsync(
        AddStudioMemberCommand command,
        CancellationToken cancellationToken = default)
    {
        var emailValidation = StudioValidators.ValidateAddMemberCommand(command);
        if (emailValidation.IsFailure)
        {
            return Result.Failure<StudioTeamState>(emailValidation.Error);
        }

        var normalizedEmail = emailValidation.Value;
        var normalizedUpperEmail = normalizedEmail.ToUpperInvariant();

        var authCheck = await LoadStudioAndVerifyOwnerAsync(command.StudioId, command.ActorUserId, cancellationToken);
        if (authCheck.IsFailure)
        {
            return Result.Failure<StudioTeamState>(authCheck.Error);
        }

        var studio = authCheck.Value;

        var currentMemberCount = await _dbContext.StudioMembers
            .AsNoTracking()
            .CountAsync(m => m.StudioId == studio.Id, cancellationToken);

        if (currentMemberCount >= StudioProfileRules.MaxTeamMembers)
        {
            return Result.Failure<StudioTeamState>(
                Error.Validation(
                    "StudioTeam.LimitReached",
                    $"A studio workspace can have up to {StudioProfileRules.MaxTeamMembers} team members."));
        }

        var targetUser = await _dbContext.Users
            .FirstOrDefaultAsync(
                u => u.NormalizedEmail == normalizedUpperEmail ||
                     (u.Email != null && u.Email.ToLower() == normalizedEmail),
                cancellationToken);

        if (targetUser is null || !targetUser.IsActive || targetUser.DeletedAt is not null || !targetUser.EmailConfirmed)
        {
            return Result.Failure<StudioTeamState>(
                Error.Validation(
                    "StudioTeam.UserNotEligible",
                    "No active HireGameDev account with a confirmed email was found for that address. Ask your teammate to sign up and confirm their email first."));
        }

        var alreadyMember = await _dbContext.StudioMembers
            .AsNoTracking()
            .AnyAsync(m => m.StudioId == studio.Id && m.UserId == targetUser.Id, cancellationToken);

        if (alreadyMember)
        {
            return Result.Failure<StudioTeamState>(
                Error.Conflict("StudioTeam.AlreadyMember", "That teammate is already a member of this studio."));
        }

        var now = _clock.UtcNow;
        _dbContext.StudioMembers.Add(new StudioMember
        {
            StudioId = studio.Id,
            UserId = targetUser.Id,
            MemberRole = command.MemberRole,
            InvitedAt = now,
            AcceptedAt = now
        });

        await EnsureUserHasStudioRoleAsync(targetUser.Id, cancellationToken);

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "studio.team.member_added",
            EntityType: "studio_profile",
            EntityId: studio.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.ActorUserId,
            AfterData: new
            {
                TargetUserId = targetUser.Id,
                Role = command.MemberRole.ToString()
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await BuildTeamStateAsync(
            studio.Id,
            studio.Name,
            studio.Slug,
            studio.OwnerUserId,
            StudioMemberRole.Owner,
            cancellationToken);
    }

    public async Task<Result<StudioTeamState>> UpdateMemberRoleAsync(
        UpdateStudioMemberRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = StudioValidators.ValidateUpdateMemberRoleCommand(command);
        if (validation.IsFailure)
        {
            return Result.Failure<StudioTeamState>(validation.Error);
        }

        var authCheck = await LoadStudioAndVerifyOwnerAsync(command.StudioId, command.ActorUserId, cancellationToken);
        if (authCheck.IsFailure)
        {
            return Result.Failure<StudioTeamState>(authCheck.Error);
        }

        var studio = authCheck.Value;

        if (command.TargetUserId == studio.OwnerUserId)
        {
            return Result.Failure<StudioTeamState>(
                Error.Validation("StudioTeam.CannotModifyOwner", "The studio Owner's role cannot be changed."));
        }

        var targetMember = await _dbContext.StudioMembers
            .FirstOrDefaultAsync(
                m => m.StudioId == studio.Id && m.UserId == command.TargetUserId,
                cancellationToken);

        if (targetMember is null)
        {
            return Result.Failure<StudioTeamState>(
                Error.NotFound("StudioTeam.MemberNotFound", "Team member was not found in this studio."));
        }

        if (targetMember.MemberRole == StudioMemberRole.Owner)
        {
            return Result.Failure<StudioTeamState>(
                Error.Validation("StudioTeam.CannotModifyOwner", "The studio Owner's role cannot be changed."));
        }

        if (targetMember.MemberRole != command.NewRole)
        {
            var previousRole = targetMember.MemberRole;
            targetMember.MemberRole = command.NewRole;

            _auditLogger.Enqueue(new AuditLogEntry(
                Action: "studio.team.member_role_updated",
                EntityType: "studio_profile",
                EntityId: studio.Id.ToString(),
                ActorType: AuditActorType.User,
                ActorUserId: command.ActorUserId,
                BeforeData: new { TargetUserId = command.TargetUserId, Role = previousRole.ToString() },
                AfterData: new { TargetUserId = command.TargetUserId, Role = command.NewRole.ToString() }));

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return await BuildTeamStateAsync(
            studio.Id,
            studio.Name,
            studio.Slug,
            studio.OwnerUserId,
            StudioMemberRole.Owner,
            cancellationToken);
    }

    public async Task<Result<StudioTeamState>> RemoveMemberAsync(
        RemoveStudioMemberCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ActorUserId == Guid.Empty ||
            command.StudioId == Guid.Empty ||
            command.TargetUserId == Guid.Empty)
        {
            return Result.Failure<StudioTeamState>(
                Error.Validation("StudioTeam.InvalidIdentity", "Valid studio and member identifiers are required."));
        }

        var authCheck = await LoadStudioAndVerifyOwnerAsync(command.StudioId, command.ActorUserId, cancellationToken);
        if (authCheck.IsFailure)
        {
            return Result.Failure<StudioTeamState>(authCheck.Error);
        }

        var studio = authCheck.Value;

        if (command.TargetUserId == studio.OwnerUserId)
        {
            return Result.Failure<StudioTeamState>(
                Error.Validation("StudioTeam.CannotRemoveOwner", "The studio Owner cannot be removed from the studio."));
        }

        var targetMember = await _dbContext.StudioMembers
            .FirstOrDefaultAsync(
                m => m.StudioId == studio.Id && m.UserId == command.TargetUserId,
                cancellationToken);

        if (targetMember is null)
        {
            return Result.Failure<StudioTeamState>(
                Error.NotFound("StudioTeam.MemberNotFound", "Team member was not found in this studio."));
        }

        if (targetMember.MemberRole == StudioMemberRole.Owner)
        {
            return Result.Failure<StudioTeamState>(
                Error.Validation("StudioTeam.CannotRemoveOwner", "The studio Owner cannot be removed from the studio."));
        }

        var removedRole = targetMember.MemberRole;
        _dbContext.StudioMembers.Remove(targetMember);

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "studio.team.member_removed",
            EntityType: "studio_profile",
            EntityId: studio.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.ActorUserId,
            BeforeData: new { TargetUserId = command.TargetUserId, Role = removedRole.ToString() }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await BuildTeamStateAsync(
            studio.Id,
            studio.Name,
            studio.Slug,
            studio.OwnerUserId,
            StudioMemberRole.Owner,
            cancellationToken);
    }

    private async Task<Result<(Guid Id, string Name, string Slug, Guid OwnerUserId)>> LoadStudioAndVerifyOwnerAsync(
        Guid studioId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var studio = await _dbContext.StudioProfiles
            .AsNoTracking()
            .Where(s => s.Id == studioId && s.DeletedAt == null)
            .Select(s => new { s.Id, s.Name, s.Slug, s.OwnerUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (studio is null)
        {
            return Result.Failure<(Guid, string, string, Guid)>(
                Error.NotFound("StudioProfile.NotFound", "Studio profile was not found."));
        }

        var callerMember = await _dbContext.StudioMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.StudioId == studio.Id && m.UserId == actorUserId,
                cancellationToken);

        if (callerMember is null || !StudioProfileRules.CanManageTeam(callerMember.MemberRole))
        {
            return Result.Failure<(Guid, string, string, Guid)>(
                Error.Forbidden("StudioTeam.OwnerOnly", "Only the studio Owner can manage team members."));
        }

        return Result.Success((studio.Id, studio.Name, studio.Slug, studio.OwnerUserId));
    }

    private async Task<Result<StudioTeamState>> BuildTeamStateAsync(
        Guid studioId,
        string studioName,
        string studioSlug,
        Guid ownerUserId,
        StudioMemberRole callerRole,
        CancellationToken cancellationToken)
    {
        var memberRows = await _dbContext.StudioMembers
            .AsNoTracking()
            .Where(m => m.StudioId == studioId)
            .Join(
                _dbContext.Users.AsNoTracking(),
                m => m.UserId,
                u => u.Id,
                (m, u) => new
                {
                    m.UserId,
                    u.DisplayName,
                    Email = u.Email ?? string.Empty,
                    m.MemberRole,
                    m.InvitedAt,
                    m.AcceptedAt
                })
            .ToListAsync(cancellationToken);

        var items = memberRows
            .OrderBy(r => r.MemberRole == StudioMemberRole.Owner ? 0 : r.MemberRole == StudioMemberRole.Recruiter ? 1 : 2)
            .ThenBy(r => r.InvitedAt)
            .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(r => new StudioTeamMemberItem(
                UserId: r.UserId,
                DisplayName: r.DisplayName,
                Email: r.Email,
                MemberRole: r.MemberRole,
                InvitedAt: r.InvitedAt,
                AcceptedAt: r.AcceptedAt,
                IsOwner: r.UserId == ownerUserId || r.MemberRole == StudioMemberRole.Owner))
            .ToArray();

        return Result.Success(new StudioTeamState(
            StudioId: studioId,
            StudioName: studioName,
            StudioSlug: studioSlug,
            CallerRole: callerRole,
            CanManageTeam: StudioProfileRules.CanManageTeam(callerRole),
            Members: items));
    }

    private async Task EnsureUserHasStudioRoleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var normalizedStudioRole = HireGameDevRoleNames.Studio.ToUpperInvariant();
        var studioRole = await _dbContext.Roles
            .FirstOrDefaultAsync(r => r.NormalizedName == normalizedStudioRole, cancellationToken);

        if (studioRole is null)
        {
            studioRole = new ApplicationRole
            {
                Id = Guid.NewGuid(),
                Name = HireGameDevRoleNames.Studio,
                NormalizedName = normalizedStudioRole,
                ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            _dbContext.Roles.Add(studioRole);
        }

        var hasRole = await _dbContext.UserRoles
            .AnyAsync(ur => ur.UserId == userId && ur.RoleId == studioRole.Id, cancellationToken);

        if (!hasRole)
        {
            _dbContext.UserRoles.Add(new IdentityUserRole<Guid>
            {
                UserId = userId,
                RoleId = studioRole.Id
            });
        }
    }
}
