using Bakery.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}

    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<DowntimeReason> DowntimeReasons => Set<DowntimeReason>();

    public DbSet<DefectTypeDef> DefectTypes => Set<DefectTypeDef>();

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<ProductRecipe> ProductRecipes => Set<ProductRecipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<ProductTolerance> ProductTolerances => Set<ProductTolerance>();
    public DbSet<ProductSegment> ProductSegments => Set<ProductSegment>();
    public DbSet<ProductDensityDefaults> ProductDensityDefaults => Set<ProductDensityDefaults>();

    public DbSet<Run> Runs => Set<Run>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<BatchRecipeIngredient> BatchRecipeIngredients => Set<BatchRecipeIngredient>();
    public DbSet<RunOverride> RunOverrides => Set<RunOverride>();
    public DbSet<WeightSample> WeightSamples => Set<WeightSample>();
    public DbSet<BatchWasteEvent> BatchWasteEvents => Set<BatchWasteEvent>();

    public DbSet<MeasurementEvent> MeasurementEvents => Set<MeasurementEvent>();
    public DbSet<VisualDefectEvent> VisualDefectEvents => Set<VisualDefectEvent>();
    public DbSet<EncoderEvent> EncoderEvents => Set<EncoderEvent>();
    public DbSet<OperatorEvent> OperatorEvents => Set<OperatorEvent>();

    public DbSet<AnalyticsBucket20> AnalyticsBuckets20 => Set<AnalyticsBucket20>();

    public DbSet<OperatorPrompt> OperatorPrompts => Set<OperatorPrompt>();
    public DbSet<PieceLink> PieceLinks => Set<PieceLink>();

    public DbSet<Alert> Alerts => Set<Alert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);


        modelBuilder.Entity<AuditLog>()
            .HasIndex(x => x.TsUtc);

        modelBuilder.Entity<AuditLog>()
            .HasIndex(x => new { x.UserEmail, x.TsUtc });

        modelBuilder.Entity<AuditLog>()
            .HasIndex(x => new { x.EntityType, x.TsUtc });


        modelBuilder.Entity<User>()
            .HasIndex(x => x.Email)
            .IsUnique();

        modelBuilder.Entity<Ingredient>()
            .HasIndex(x => x.Code)
            .IsUnique();

        modelBuilder.Entity<Ingredient>()
            .HasIndex(x => x.ItemNumber)
            .IsUnique();

        modelBuilder.Entity<DowntimeReason>()
            .HasIndex(x => x.SortOrder);

        modelBuilder.Entity<DefectTypeDef>()
            .HasIndex(x => x.Code)
            .IsUnique();

        modelBuilder.Entity<DefectTypeDef>()
            .HasIndex(x => x.SortOrder);

        modelBuilder.Entity<ProductDensityDefaults>()
            .HasOne(x => x.Product)
            .WithOne(x => x.DensityDefaults)
            .HasForeignKey<ProductDensityDefaults>(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProductTolerance>()
            .HasIndex(x => new { x.ProductId, x.Point });

        modelBuilder.Entity<ProductSegment>()
            .HasIndex(x => new { x.ProductId, x.SegmentId })
            .IsUnique();

        modelBuilder.Entity<ProductRecipe>()
            .HasIndex(x => new { x.ProductId, x.Version });

        modelBuilder.Entity<ProductRecipe>()
            .HasOne(x => x.Product)
            .WithMany(x => x.Recipes)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RecipeIngredient>()
            .HasIndex(x => new { x.RecipeId, x.IngredientId })
            .IsUnique();

        modelBuilder.Entity<RecipeIngredient>()
            .HasOne(x => x.Recipe)
            .WithMany(x => x.Ingredients)
            .HasForeignKey(x => x.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RecipeIngredient>()
            .HasOne(x => x.Ingredient)
            .WithMany()
            .HasForeignKey(x => x.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Batch>()
            .HasIndex(x => new { x.RunId, x.BatchNumber })
            .IsUnique();

        modelBuilder.Entity<BatchRecipeIngredient>()
            .HasOne(x => x.Batch)
            .WithMany()
            .HasForeignKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BatchRecipeIngredient>()
            .HasOne(x => x.Ingredient)
            .WithMany()
            .HasForeignKey(x => x.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BatchRecipeIngredient>()
            .HasIndex(x => new { x.BatchId, x.IngredientId });

        modelBuilder.Entity<MeasurementEvent>()
            .HasIndex(x => new { x.RunId, x.Point, x.TsUtc });

        modelBuilder.Entity<VisualDefectEvent>()
            .HasIndex(x => new { x.RunId, x.TsUtc });

        modelBuilder.Entity<VisualDefectEvent>()
            .HasIndex(x => new { x.RunId, x.PieceSeqIndex });

        modelBuilder.Entity<EncoderEvent>()
            .HasIndex(x => new { x.RunId, x.SegmentId, x.TsUtc });

        modelBuilder.Entity<OperatorEvent>()
            .HasIndex(x => new { x.RunId, x.TsUtc });

        modelBuilder.Entity<AnalyticsBucket20>()
            .HasIndex(x => new { x.RunId, x.BucketStartUtc })
            .IsUnique();

        modelBuilder.Entity<AnalyticsBucket20>()
            .Property(x => x.P1Json)
            .HasColumnType("jsonb");
        modelBuilder.Entity<AnalyticsBucket20>()
            .Property(x => x.P2Json)
            .HasColumnType("jsonb");
        modelBuilder.Entity<AnalyticsBucket20>()
            .Property(x => x.VisParetoJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<AnalyticsBucket20>()
            .Property(x => x.P3Json)
            .HasColumnType("jsonb");

        modelBuilder.Entity<OperatorPrompt>()
            .HasIndex(x => new { x.RunId, x.Type, x.Status });

        modelBuilder.Entity<OperatorPrompt>()
            .HasOne(x => x.Run)
            .WithMany()
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PieceLink>()
            .HasIndex(x => new { x.RunId, x.FromPieceUid, x.ToPieceUid })
            .IsUnique();

        modelBuilder.Entity<Alert>()
            .HasIndex(x => new { x.RunId, x.Type, x.Status });

        modelBuilder.Entity<Alert>()
            .HasIndex(x => x.DedupeKey);

        modelBuilder.Entity<RunOverride>()
            .HasOne(x => x.Run)
            .WithOne(x => x.Override)
            .HasForeignKey<RunOverride>(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        // IMPORTANT: We intentionally keep raw events long-term.
    }
}
