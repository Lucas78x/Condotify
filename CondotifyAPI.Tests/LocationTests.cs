using CondotifyAPI.Models;

namespace CondotifyAPI.Tests
{
    public class LocationTests
    {
        [Fact]
        public void Create_ShouldReturnLocationWithCorrectProperties()
        {
            // Arrange
            float x = 10.5f;
            float y = 20.3f;
            string name = "SP";

            // Act
            var location = Location.Create(name, x, y);

            // Assert
            Assert.NotNull(location);
            Assert.Equal(x, location.X);
            Assert.Equal(y, location.Y);
            Assert.NotEqual(Guid.Empty, location.Id);
        }

        [Fact]
        public void Create_ShouldGenerateUniqueIdForEachLocation()
        {
            // Act
            var location1 = Location.Create("SP", 1f, 2f);
            var location2 = Location.Create("SP", 3f, 4f);

            // Assert
            Assert.NotEqual(location1.Id, location2.Id);
        }

        [Fact]
        public void Create_WithZeroCoordinates_ShouldSetPropertiesCorrectly()
        {
            // Act
            var location = Location.Create("SP", 0f, 0f);

            // Assert
            Assert.Equal(0f, location.X);
            Assert.Equal(0f, location.Y);
        }
    }
}
