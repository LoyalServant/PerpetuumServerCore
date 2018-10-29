using System.Numerics;

namespace Perpetuum.Zones.Terrains
{
    public interface IAltitudeLayer : ILayer<ushort>
    {
        
    }

    public class AltitudeLayer : Layer<ushort>,IAltitudeLayer
    {
        public AltitudeLayer(ushort[] rawData, int width, int height) : base(LayerType.Altitude, rawData, width, height)
        {
        }

        public ushort GetAltitude(int x, int y)
        {
            // 0 ... 2047
            return (ushort)(GetValue(x,y) >> 5);
        }

        public double GetAltitudeAsDouble(Position position)
        {
            return GetAltitudeAsDouble((int) position.X, (int) position.Y);
        }

        public double GetAltitudeAsDouble(Vector3 position)
        {
            return GetAltitudeAsDouble((int) position.X, (int) position.Y);
        }

        public double GetAltitudeAsDouble(int x, int y)
        {
            return GetValue(x,y) / 32.0;
        }
    }
}