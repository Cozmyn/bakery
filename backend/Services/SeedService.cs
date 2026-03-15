using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Services;

public static class SeedService
{
    public static async Task EnsureSeedAsync(AppDbContext db)
    {
        // DEV convenience: create schema if missing (we'll move to migrations in ETAPA 3)
        await db.Database.EnsureCreatedAsync();

        if (!await db.Users.AnyAsync())
        {
            var admin = new User
            {
                Email = "admin@local",
                PasswordHash = PasswordService.Hash("Admin123!"),
                Role = UserRole.Admin,
                CreatedBy = "seed",
                Source = "seed"
            };
            var op = new User
            {
                Email = "operator@local",
                PasswordHash = PasswordService.Hash("Operator123!"),
                Role = UserRole.Operator,
                CreatedBy = "seed",
                Source = "seed"
            };
            db.Users.AddRange(admin, op);
        }

        if (!await db.Products.AnyAsync())
        {
            var p = new Product
            {
                Code = "BREAD-001",
                Name = "Demo Bread",
                CostPerUnit = 1.20m,
                ValuePerUnit = 2.50m,
                CostPerHour = 120m,
                IdealCycleTimeSec = 2,
                TargetSpeedDefaultMps = 0.8m,
                ProofingMinMinutes = 40,
                ProofingMaxMinutes = 70,
                NominalUnitWeightG_P3 = 500m,
                VisToP3Minutes = 35,
                PublishedAtUtc = DateTime.UtcNow,
                CreatedBy = "seed",
                Source = "seed"
            };
            db.Products.Add(p);

            db.ProductDensityDefaults.Add(new ProductDensityDefaults
            {
                ProductId = p.Id,
                DensityP1_GPerCm3 = 0.75m,
                DensityP2_GPerCm3 = 0.60m,
                DensityP3_GPerCm3 = 0.55m,
                CreatedBy = "seed",
                Source = "seed"
            });

            foreach (var point in new[] { PointCode.P1, PointCode.P2, PointCode.P3 })
            {
                db.ProductTolerances.Add(new ProductTolerance
                {
                    ProductId = p.Id,
                    Point = point,
                    WidthMinMm = 60, WidthMaxMm = 85,
                    LengthMinMm = 140, LengthMaxMm = 180,
                    HeightMinMm = 45, HeightMaxMm = 70,
                    VolumeMinMm3 = 250_000, VolumeMaxMm3 = 500_000,
                    WeightMinG = 470, WeightMaxG = 530,
                    CreatedBy = "seed",
                    Source = "seed"
                });
            }

            for (int seg = 1; seg <= 4; seg++)
            {
                db.ProductSegments.Add(new ProductSegment
                {
                    ProductId = p.Id,
                    SegmentId = seg,
                    LengthM = seg switch { 1 => 12, 2 => 8, 3 => 15, _ => 10 },
                    TargetSpeedMps = 0.8m,
                    CreatedBy = "seed",
                    Source = "seed"
                });
            }

            // Recipe will be added after ingredient seed (below)
        }

        if (!await db.Ingredients.AnyAsync())
        {
            db.Ingredients.AddRange(
                new Ingredient { ItemNumber = 1001, Code = "1001", Name = "Flour", DefaultUnit = "kg", CreatedBy = "seed", Source = "seed" },
                new Ingredient { ItemNumber = 1002, Code = "1002", Name = "Water", DefaultUnit = "kg", CreatedBy = "seed", Source = "seed" },
                new Ingredient { ItemNumber = 1003, Code = "1003", Name = "Yeast", DefaultUnit = "kg", CreatedBy = "seed", Source = "seed" },
                new Ingredient { ItemNumber = 1004, Code = "1004", Name = "Salt", DefaultUnit = "kg", CreatedBy = "seed", Source = "seed" }
            );
        }

        // Ensure at least one recipe for demo product
        var demo = await db.Products.AsNoTracking().OrderBy(x => x.Code).FirstOrDefaultAsync();
        if (demo is not null)
        {
            var hasRecipe = await db.ProductRecipes.AnyAsync(x => x.ProductId == demo.Id && x.IsCurrent);
            if (!hasRecipe)
            {
                var flour = await db.Ingredients.FirstAsync(x => x.ItemNumber == 1001);
                var water = await db.Ingredients.FirstAsync(x => x.ItemNumber == 1002);
                var yeast = await db.Ingredients.FirstAsync(x => x.ItemNumber == 1003);
                var salt = await db.Ingredients.FirstAsync(x => x.ItemNumber == 1004);

                var recipe = new ProductRecipe
                {
                    ProductId = demo.Id,
                    Version = 1,
                    IsCurrent = true,
                    CreatedBy = "seed",
                    Source = "seed"
                };
                db.ProductRecipes.Add(recipe);
                db.RecipeIngredients.AddRange(
                    new RecipeIngredient { RecipeId = recipe.Id, IngredientId = flour.Id, Quantity = 50m, Unit = "kg", CreatedBy = "seed", Source = "seed" },
                    new RecipeIngredient { RecipeId = recipe.Id, IngredientId = water.Id, Quantity = 32m, Unit = "kg", CreatedBy = "seed", Source = "seed" },
                    new RecipeIngredient { RecipeId = recipe.Id, IngredientId = yeast.Id, Quantity = 0.4m, Unit = "kg", CreatedBy = "seed", Source = "seed" },
                    new RecipeIngredient { RecipeId = recipe.Id, IngredientId = salt.Id, Quantity = 0.9m, Unit = "kg", CreatedBy = "seed", Source = "seed" }
                );
            }
        }

        if (!await db.DowntimeReasons.AnyAsync())
        {
            // One-tap buttons (Operator UX)
            db.DowntimeReasons.AddRange(
                new DowntimeReason { Code = "NO_DOUGH", Label = "Waiting for dough", Category = "MATERIAL", IsOneTap = true, SortOrder = 10, CreatedBy = "seed", Source = "seed" },
                new DowntimeReason { Code = "CLEANING", Label = "Cleaning", Category = "SANITATION", IsOneTap = true, SortOrder = 20, CreatedBy = "seed", Source = "seed" },
                new DowntimeReason { Code = "CHANGEOVER", Label = "Changeover", Category = "CHANGEOVER", IsOneTap = true, SortOrder = 30, CreatedBy = "seed", Source = "seed" },
                new DowntimeReason { Code = "MAINT", Label = "Maintenance", Category = "MAINTENANCE", IsOneTap = true, SortOrder = 40, CreatedBy = "seed", Source = "seed" },
                new DowntimeReason { Code = "BLOCKAGE", Label = "Blockage/Jam", Category = "LINE", IsOneTap = true, SortOrder = 50, CreatedBy = "seed", Source = "seed" },
                new DowntimeReason { Code = "QUALITY_HOLD", Label = "Quality hold", Category = "QUALITY", IsOneTap = true, SortOrder = 60, CreatedBy = "seed", Source = "seed" },
                new DowntimeReason { Code = "OTHER", Label = "Other", Category = "GENERAL", IsOneTap = true, SortOrder = 999, CreatedBy = "seed", Source = "seed" }
            );
        }

        if (!await db.DefectTypes.AnyAsync())
        {
            // Camera defect classifications (events store codes; UI renders labels/categories).
            db.DefectTypes.AddRange(
                new DefectTypeDef { Code = "BURNED", Label = "Ars", Category = "BAKE", SeverityDefault = 3, SortOrder = 10, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "UNDERBAKED", Label = "Necopt", Category = "BAKE", SeverityDefault = 3, SortOrder = 20, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "TOO_DARK", Label = "Prea inchis", Category = "BAKE", SeverityDefault = 2, SortOrder = 30, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "TOO_LIGHT", Label = "Prea deschis", Category = "BAKE", SeverityDefault = 2, SortOrder = 40, CreatedBy = "seed", Source = "seed" },

                new DefectTypeDef { Code = "NO_SCORING", Label = "Fara scoring", Category = "SURFACE", SeverityDefault = 2, SortOrder = 60, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "CRACKED", Label = "Crapata", Category = "SHAPE", SeverityDefault = 2, SortOrder = 70, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "DEFORMED", Label = "Deformata", Category = "SHAPE", SeverityDefault = 2, SortOrder = 80, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "COLLAPSED", Label = "Colapsata", Category = "SHAPE", SeverityDefault = 3, SortOrder = 90, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "TORN", Label = "Rupta", Category = "SHAPE", SeverityDefault = 2, SortOrder = 95, CreatedBy = "seed", Source = "seed" },

                new DefectTypeDef { Code = "UNEVEN_COLOR", Label = "Culoare neuniforma", Category = "SURFACE", SeverityDefault = 1, SortOrder = 110, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "BLISTERS", Label = "Basicata", Category = "SURFACE", SeverityDefault = 1, SortOrder = 120, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "HOLES", Label = "Gauri", Category = "SURFACE", SeverityDefault = 2, SortOrder = 130, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "STUCK", Label = "Lipita", Category = "HANDLING", SeverityDefault = 2, SortOrder = 140, CreatedBy = "seed", Source = "seed" },

                new DefectTypeDef { Code = "CONTAMINATION", Label = "Contaminare", Category = "SAFETY", SeverityDefault = 3, SortOrder = 180, CreatedBy = "seed", Source = "seed" },
                

                new DefectTypeDef { Code = "SCORING_TOO_DEEP", Label = "Scoring prea adanc", Category = "SURFACE", SeverityDefault = 1, SortOrder = 145, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "SCORING_TOO_SHALLOW", Label = "Scoring prea superficial", Category = "SURFACE", SeverityDefault = 1, SortOrder = 146, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "SIDE_SPLIT", Label = "Crapata pe lateral", Category = "SHAPE", SeverityDefault = 2, SortOrder = 150, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "BURST", Label = "Explodata", Category = "SHAPE", SeverityDefault = 2, SortOrder = 151, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "FLAT", Label = "Prea plata", Category = "SHAPE", SeverityDefault = 2, SortOrder = 152, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "TOO_ROUND", Label = "Prea rotunda", Category = "SHAPE", SeverityDefault = 1, SortOrder = 153, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "STICKY_SURFACE", Label = "Suprafata lipicioasa", Category = "HANDLING", SeverityDefault = 2, SortOrder = 160, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "DOUBLE", Label = "Dubla/Lipite", Category = "HANDLING", SeverityDefault = 2, SortOrder = 161, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "FOREIGN_OBJECT", Label = "Corp strain", Category = "SAFETY", SeverityDefault = 3, SortOrder = 170, CreatedBy = "seed", Source = "seed" },
                new DefectTypeDef { Code = "MOLD_SPOT", Label = "Pata de mucegai", Category = "SAFETY", SeverityDefault = 3, SortOrder = 171, CreatedBy = "seed", Source = "seed" },
new DefectTypeDef { Code = "OTHER", Label = "Alt defect", Category = "OTHER", SeverityDefault = 1, SortOrder = 999, CreatedBy = "seed", Source = "seed" }
            );
        }

        await db.SaveChangesAsync();
    }
}
