using Ecosphere.Planet;
using UnityEngine;

namespace Ecosphere.Authoring
{
    [CreateAssetMenu(menuName="Ecosphere/World Palette")]
    public class WorldPalette : ScriptableObject
    {
        public Gradient[] BiomeGradients = new Gradient[12];
        public Color ColorFor(Biome biome,float latitude,float season)
        {
            int index=(int)biome;
            Color baseColor=BiomeGradients!=null && index<BiomeGradients.Length && BiomeGradients[index]!=null
                ? BiomeGradients[index].Evaluate(Mathf.Abs(latitude)) : DefaultColor(biome);
            return Color.Lerp(baseColor,Color.white,season*.25f);
        }
        public static Color DefaultColor(Biome biome)
        {
            switch(biome)
            {
                case Biome.Ocean: case Biome.OpenOcean: return new Color(.035f,.19f,.32f);
                case Biome.Reef: return new Color(.08f,.48f,.56f);
                case Biome.Beach: return new Color(.83f,.73f,.48f);
                case Biome.Desert: return new Color(.8f,.58f,.31f);
                case Biome.Savanna: return new Color(.62f,.67f,.3f);
                case Biome.Forest: return new Color(.13f,.38f,.25f);
                case Biome.Taiga: return new Color(.21f,.36f,.34f);
                case Biome.Tundra: return new Color(.55f,.62f,.57f);
                case Biome.Glacier: return new Color(.85f,.92f,.96f);
                case Biome.Wetland: return new Color(.25f,.46f,.32f);
                default: return new Color(.35f,.63f,.34f);
            }
        }
    }
}
