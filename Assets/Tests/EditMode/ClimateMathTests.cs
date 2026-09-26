using Ecosphere.Planet;
using NUnit.Framework;
using Unity.Mathematics;

public class ClimateMathTests
{
    [Test]
    public void PressureWindMovesDownGradient()
    {
        float3 normal = new float3(0, 1, 0);
        float3 pressureGradient = new float3(1, 0, 0);
        float3 wind = ClimateMath.WindDownGradient(normal, pressureGradient, 0, 10);
        Assert.Greater(math.dot(wind, -pressureGradient), 0f);
    }

    [Test]
    public void CoriolisDeflectsToOppositeSidesAcrossEquator()
    {
        float3 northNormal = new float3(math.cos(.6f), math.sin(.6f), 0);
        float3 southNormal = new float3(math.cos(-.6f), math.sin(-.6f), 0);
        float3 east = new float3(0, 0, 1);
        float3 north = new float3(-math.sin(.6f), math.cos(.6f), 0);
        float3 south = new float3(-math.sin(-.6f), math.cos(-.6f), 0);
        float3 northWind = ClimateMath.DeflectByCoriolis(northNormal, east, .6f);
        float3 southWind = ClimateMath.DeflectByCoriolis(southNormal, east, -.6f);
        Assert.Less(math.dot(northWind, north), 0f);
        Assert.Greater(math.dot(southWind, south), 0f);
    }

    [Test]
    public void PairwiseAdvectionConservesHumidity()
    {
        float a = .8f, b = .2f, flow = .03f;
        float net = -ClimateMath.HumidityEdgeFlux(a, b, flow)
            -ClimateMath.HumidityEdgeFlux(b, a, -flow);
        Assert.AreEqual(0f, net, 1e-6f);
    }

    [Test]
    public void SnowAccumulatesBelowFreezingAndMeltsAboveIt()
    {
        float snow = ClimateMath.SnowStep(.1f, 20f, -5f, .02f);
        Assert.Greater(snow, .1f);
        float melted = ClimateMath.SnowStep(snow, 0f, 5f, .02f);
        Assert.Less(melted, snow);
        Assert.GreaterOrEqual(melted, 0f);
    }

    [Test]
    public void OrographicLiftRequiresWindwardRise()
    {
        Assert.Greater(ClimateMath.OrographicLift(.8f, .2f, 8f), 0f);
        Assert.AreEqual(0f, ClimateMath.OrographicLift(.2f, .8f, 8f));
    }

    [Test]
    public void OceanCurrentRespondsToWindAndStaysTangent()
    {
        float3 normal = new float3(0, 1, 0);
        float3 wind = new float3(0, 0, 8);
        float3 current = ClimateMath.WindDrivenCurrent(normal, wind, float3.zero);
        Assert.Greater(math.dot(current, wind), 0f);
        Assert.AreEqual(0f, math.dot(current, normal), 1e-5f);
    }

    [Test]
    public void WeatherDetectionHasExplicitThresholds()
    {
        Assert.IsTrue(ClimateMath.IsStormCandidate(.6f, .7f));
        Assert.IsFalse(ClimateMath.IsStormCandidate(.4f, .7f));
        Assert.IsTrue(ClimateMath.IsFogCandidate(.95f, .2f, .8f));
        Assert.IsFalse(ClimateMath.IsFogCandidate(.95f, 2f, .8f));
        Assert.IsTrue(ClimateMath.IsDroughtCandidate(3000, 1000, .1f));
        Assert.IsFalse(ClimateMath.IsDroughtCandidate(1000, 1000, .1f));
    }

    [Test]
    public void EffectiveTemperatureAppliesWindChillAndHumidHeat()
    {
        Assert.Less(ClimateMath.EffectiveTemperature(10f, 10f, .5f), 10f);
        Assert.Greater(ClimateMath.EffectiveTemperature(35f, 0f, 1f), 35f);
    }
}
