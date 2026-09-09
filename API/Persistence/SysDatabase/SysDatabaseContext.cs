using Microsoft.EntityFrameworkCore;
using API.Persistence.SysDatabase.Entity;

namespace API.Persistence.SysDatabase;

/// <summary>
/// 系統資料庫上下文。
/// </summary>
public sealed class SysDatabaseContext : DbContext
{
    /// <summary>
    /// 初始化 SysDatabaseContext 的新執行個體。
    /// </summary>
    public SysDatabaseContext(DbContextOptions<SysDatabaseContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// LLM 呼叫日誌資料表。
    /// </summary>
    public DbSet<SysmLlmLog> SysmLlmLogs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 設定複合主鍵
        modelBuilder.Entity<SysmLlmLog>()
            .HasKey(log => new { log.SysId, log.SessionId, log.QuerySeq });
    }
}
