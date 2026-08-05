using PosSystem.Web.Data;
using PosSystem.Web.Models.Entities;

namespace PosSystem.Web.Services;

public interface IAuditService
{
    /// <summary>يضيف السجل إلى الـ ChangeTracker (يحتاج SaveChanges من المستدعي)</summary>
    void Track(string action, string entityName, string? entityId, string? details = null);

    /// <summary>يضيف السجل ويحفظه فورًا</summary>
    Task LogAsync(string action, string entityName, string? entityId, string? details = null);
}

public class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public AuditService(AppDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public void Track(string action, string entityName, string? entityId, string? details = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser.UserId,
            UserName = _currentUser.DisplayName,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Details = details,
            IpAddress = _currentUser.IpAddress,
            CreatedAt = DateTime.Now
        });
    }

    public async Task LogAsync(string action, string entityName, string? entityId, string? details = null)
    {
        Track(action, entityName, entityId, details);
        await _db.SaveChangesAsync();
    }
}
