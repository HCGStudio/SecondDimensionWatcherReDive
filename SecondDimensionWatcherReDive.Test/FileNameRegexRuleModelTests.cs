using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.Inference;
using AppDbContext = SecondDimensionWatcherReDive.Models.ApplicationContext;
using FileNameRegexRuleEntity = SecondDimensionWatcherReDive.Models.FileNameRegexRule;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class FileNameRegexRuleModelTests
{
    [TestMethod]
    public void Model_ConstrainsPatternAndCascadesWithAnimation()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .Options;
        using var context = new AppDbContext(options);

        var entity = context.Model.FindEntityType(typeof(FileNameRegexRuleEntity));
        Assert.IsNotNull(entity);

        var pattern = entity.FindProperty(nameof(FileNameRegexRuleEntity.Pattern));
        Assert.IsNotNull(pattern);
        Assert.AreEqual(FileNameRegexMatcher.MaxPatternLength, pattern.GetMaxLength());

        var uniqueScopeIndex = entity.GetIndexes().Single(index => index.IsUnique);
        CollectionAssert.AreEqual(
            new[]
            {
                nameof(FileNameRegexRuleEntity.AnimationId),
                nameof(FileNameRegexRuleEntity.Pattern)
            },
            uniqueScopeIndex.Properties.Select(property => property.Name).ToArray());

        var animationForeignKey = entity.GetForeignKeys().Single();
        Assert.AreEqual(DeleteBehavior.Cascade, animationForeignKey.DeleteBehavior);
        Assert.AreEqual(typeof(SecondDimensionWatcherReDive.Models.Animation),
            animationForeignKey.PrincipalEntityType.ClrType);
    }
}
