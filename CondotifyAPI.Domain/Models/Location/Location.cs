
namespace CondotifyAPI.Domain.Models
{
    public class Location
    {
        public Location() { }

        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }

        private Location(string name,float x, float y)
        {
            Id = Guid.NewGuid();
            SetName(name);
            SetX(x);
            SetY(y);

        }

        public static Location Create(string name,float x, float y)
        {
            return new Location(name,x, y);
        }

        public bool Update(string name, float x, float y)
        {
            SetName(name);
            SetX(x);
            SetY(y);

            return true;
        }

        private void SetName(string name) => Name = name;
        private void SetX(float x) => X = x;
        private void SetY(float y) => Y = y;
    }
}
