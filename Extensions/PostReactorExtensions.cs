using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Models;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Extensions;

public static class PostReactorExtensions
{
    /// <summary>
    /// Verifies whether a viewer can access a post based on its visibility and follow relationship.
    /// </summary>
    public static async Task<bool> CanUserViewPostAsync(
        this AppDbContext dbContext,
        PostVisibility visibility,
        int authorUserId,
        int? currentUserId)
    {
        if (visibility == PostVisibility.Public)
        {
            return true;
        }

        if (!currentUserId.HasValue)
        {
            return false;
        }

        if (authorUserId == currentUserId.Value)
        {
            return true;
        }

        if (visibility == PostVisibility.Followers)
        {
            return await dbContext.Follows
                .AsNoTracking()
                .AnyAsync(f => f.FollowerId == currentUserId.Value && f.FolloweeId == authorUserId);
        }

        return false;
    }

    /// <summary>
    /// Materializes a paginated UserSearchDto query and hydrates IsFollowing flags in a single lookup.
    /// </summary>
    public static async Task<PagedResult<UserSearchDto>> ToPagedFollowAwareResultAsync(
        this IQueryable<UserSearchDto> query,
        AppDbContext dbContext,
        int? currentUserId,
        PaginationParams pagination)
    {
        var totalCount = await query.CountAsync();
        var skip = (pagination.PageNumber - 1) * pagination.PageSize;

        var items = await query
            .Skip(skip)
            .Take(pagination.PageSize)
            .ToListAsync();

        if (currentUserId.HasValue && items.Count > 0)
        {
            var itemIds = items.Select(u => u.Id).ToList();
            var followedIds = (await dbContext.Follows
                .AsNoTracking()
                .Where(f => f.FollowerId == currentUserId.Value && itemIds.Contains(f.FolloweeId))
                .Select(f => f.FolloweeId)
                .ToListAsync())
                .ToHashSet();

            foreach (var item in items)
            {
                item.IsFollowing = followedIds.Contains(item.Id);
            }
        }

        return new PagedResult<UserSearchDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize
        };
    }
}

