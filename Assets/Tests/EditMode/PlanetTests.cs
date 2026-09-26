using Ecosphere.Planet;
using NUnit.Framework;
using Unity.Mathematics;

public class PlanetTests
{
    [Test] public void TopologyIsStableAndManifold()
    {
        using(var a=Icosphere.Build(3)) using(var b=Icosphere.Build(3))
        {
            Assert.AreEqual(642,a.Value.Centers.Length);
            Assert.AreEqual(1280,a.Value.Faces.Length);
            for(int i=0;i<a.Value.Centers.Length;i++)
            {
                Assert.AreEqual(a.Value.Centers[i],b.Value.Centers[i]);
                Assert.AreEqual(a.Value.NeighborOffsets[i],b.Value.NeighborOffsets[i]);
                Assert.GreaterOrEqual(a.Value.NeighborOffsets[i+1]-a.Value.NeighborOffsets[i],5);
            }
        }
    }
    [Test] public void ElevationSameSeedBitExact()
    {
        using(var grid=Icosphere.Build(2))
        {
            var first=new uint[grid.Value.Centers.Length];
            var second=new uint[first.Length];
            for(int i=0;i<first.Length;i++)
                first[i]=math.asuint(TerrainMath.Elevation(grid.Value.Centers[i],42,.03f,.6f));
            for(int i=0;i<second.Length;i++)
                second[i]=math.asuint(TerrainMath.Elevation(grid.Value.Centers[i],42,.03f,.6f));
            CollectionAssert.AreEqual(first,second);
        }
    }
    [Test] public void SolsticesAndMidnight()
    {
        Assert.Greater(SunMath.Insolation(new float3(1,0,0),SunMath.Direction(.5f,.125f)),.99f);
        Assert.AreEqual(0,SunMath.Insolation(new float3(1,0,0),SunMath.Direction(0,.125f)));
        float3 pole=new float3(math.cos(math.radians(80f)),math.sin(math.radians(80f)),0);
        Assert.AreEqual(0,SunMath.Insolation(pole,SunMath.Direction(.5f,.875f)));
        Assert.Greater(SunMath.Insolation(pole,SunMath.Direction(0,.375f)),0);
    }
    [Test] public void ClassifierReturnsValidBiome()
    {
        var classifier=new ProxyBiomeClassifier();
        for(float lat=-1;lat<=1;lat+=.1f)
            for(float wet=0;wet<=1;wet+=.1f)
                Assert.That((int)classifier.Classify(.2f,lat,wet),Is.InRange(0,11));
    }
}
