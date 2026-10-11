using FluentAssertions;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Utils;
using Xunit;

namespace QuestionsHub.UnitTests;

public class PackageLabelsTests
{
    [Theory]
    [InlineData(PackageType.Www, TourType.Regular, "3", null, "Тур 3")]
    [InlineData(PackageType.Www, TourType.Warmup, "0", null, "Розминка")]
    [InlineData(PackageType.Www, TourType.Shootout, "П", null, "Перестрілка")]
    [InlineData(PackageType.Shvager, TourType.Regular, "2", "Космос", "Тема 2. Космос")]
    [InlineData(PackageType.Shvager, TourType.Regular, "2", null, "Тема 2")]
    public void TourName_MatchesThePackagePage(PackageType packageType, TourType tourType, string number, string? title, string expected)
    {
        var tour = new Tour { Number = number, Type = tourType, Title = title };

        PackageLabels.TourName(tour, packageType).Should().Be(expected);
    }

    [Fact]
    public void BlockNumbers_CountAcrossThePackage_InDisplayOrder()
    {
        var tour1 = new Tour { Number = "1", Blocks = [new Block { Id = 11, OrderIndex = 1 }, new Block { Id = 10, OrderIndex = 0 }] };
        var tour2 = new Tour { Number = "2" };
        var tour3 = new Tour { Number = "3", Blocks = [new Block { Id = 30, OrderIndex = 0 }] };

        var numbers = PackageLabels.BlockNumbers([tour1, tour2, tour3]);

        numbers.Should().Equal(new Dictionary<int, int> { [10] = 1, [11] = 2, [30] = 3 });
    }

    [Fact]
    public void ForDisplay_OrdersShvagerByPosition_AndWwwByNumber()
    {
        var warmup = new Tour { Number = "0", OrderIndex = 2, Type = TourType.Warmup };
        var first = new Tour { Number = "1", OrderIndex = 0 };
        var shootout = new Tour { Number = "П", OrderIndex = 1, Type = TourType.Shootout };
        Tour[] tours = [first, shootout, warmup];

        TourOrdering.ForDisplay(tours, PackageType.Www).Should().Equal(warmup, first, shootout);
        TourOrdering.ForDisplay(tours, PackageType.Shvager).Should().Equal(first, shootout, warmup);
    }
}
