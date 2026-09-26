using System.IO;
using Ecosphere.Planet;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public class ClimateYearRegressionTests
{
    [Test]
    public void Seed42Level4RunsAStableSeasonalClimateYear()
    {
        using(var topology=Icosphere.Build(4))
        {
            var cells=new NativeArray<PlanetCell>(topology.Value.Centers.Length,Allocator.Temp);
            try
            {
                for(int i=0;i<cells.Length;i++)
                {
                    PlanetCell cell=TerrainMath.Generate(topology.Value.Centers[i],42UL,.03f,.6f);
                    cell.Temperature=27f-58f*math.abs(cell.Latitude);
                    cell.Pressure=ClimateMath.Pressure(cell.Temperature,cell.Elevation,cell.Latitude);
                    cell.SoilMoisture=cell.Land != 0 ? .5f : 0f;
                    cell.OceanTemperature=cell.Land == 0 ? cell.Temperature : 0f;
                    cell.Nutrient=cell.Land == 0 ? .5f : 0f;
                    cells[i]=cell;
                }
                ClimateRegressionResult result=ClimateHeadlessRegression.Run(topology,cells,42UL,60,1200,60,2);
                Assert.IsTrue(result.AllFieldsFinite,"Climate year produced NaN or infinity.");
                Assert.Greater(result.EquatorMeanTemperature,result.PolarMeanTemperature,"The equator should be warmer than polar bands.");
                Assert.Greater(result.SummerHemisphereContrast,0f,"The summer hemisphere should be warmer than the winter hemisphere.");
                Assert.GreaterOrEqual(result.StormEventCount,5,"Expected at least five distinct storm candidates during the regression.");
                Assert.That(result.Csv,Does.StartWith("cell,latitude,longitude"));
                string outputDirectory=Path.GetFullPath(Path.Combine(Application.dataPath,"../TestResults"));
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory,"climate-seed42-60days.csv"),result.Csv);
            }
            finally {cells.Dispose();}
        }
    }
}
