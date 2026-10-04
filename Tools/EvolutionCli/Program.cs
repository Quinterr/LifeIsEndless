using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Ecosphere.Core.Simulation;

internal static class Program
{
    private static readonly (string Name, EvolutionScenarioKind Kind)[] Scenarios =
    {
        ("default", EvolutionScenarioKind.Default),
        ("ice-age", EvolutionScenarioKind.IceAgeTrend),
        ("predator-pressure", EvolutionScenarioKind.PredatorPressure),
        ("drought", EvolutionScenarioKind.Drought),
        ("mass-extinction-recovery", EvolutionScenarioKind.MassExtinctionRecovery)
    };

    private static int Main(string[] args)
    {
        try
        {
            Dictionary<string, string> options = Parse(args);
            if (options.ContainsKey("help"))
            {
                PrintHelp();
                return 0;
            }

            string selected = Get(options, "scenario", "default").ToLowerInvariant();
            string outputRoot = Get(options, "out", Path.Combine(Environment.CurrentDirectory, "evolution-output"));
            bool runAll = selected == "all";
            (string Name, EvolutionScenarioKind Kind)[] runs = runAll
                ? Scenarios
                : new[] { FindScenario(selected) };

            for (int i = 0; i < runs.Length; i++)
            {
                EvolutionScenarioSettings settings = EvolutionScenarioSettings.Create(runs[i].Kind);
                settings.WorldSeed = ParseULong(options, "seed", settings.WorldSeed);
                settings.Years = ParseInt(options, "years", settings.Years);
                settings.PopulationSize = ParseInt(options, "population", settings.PopulationSize);
                settings.RegionCount = ParseInt(options, "regions", settings.RegionCount);
                string kingdom = Get(options, "kingdom", string.Empty).ToLowerInvariant();
                if (kingdom == "plant") settings.Kingdom = GeneKingdom.Plant;
                else if (kingdom == "animal") settings.Kingdom = GeneKingdom.Animal;
                else if (kingdom.Length != 0) throw new ArgumentException("--kingdom must be 'animal' or 'plant'.");

                settings = settings.Sanitized();
                HeadlessEvolutionResult result = HeadlessEvolutionRunner.Run(settings);
                string directory = runAll ? Path.Combine(outputRoot, runs[i].Name) : outputRoot;
                WriteOutputs(directory, runs[i].Name, settings, result);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}: years={1}, population={2}, livingSpecies={3}, speciationEvents={4}, extinctions={5}, finiteMetrics={6}, output={7}",
                    runs[i].Name, settings.Years, result.FinalPopulation, result.LivingSpecies,
                    CountEvents(result, EvolutionEventKind.Speciation),
                    CountEvents(result, EvolutionEventKind.Extinction), result.HasFiniteMetrics,
                    Path.GetFullPath(directory)));
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("evolution-cli: " + exception.Message);
            Console.Error.WriteLine("Use --help for usage.");
            return 2;
        }
    }

    private static void WriteOutputs(string directory, string scenario,
        EvolutionScenarioSettings settings, HeadlessEvolutionResult result)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "metrics.csv"), result.ToCsv(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "events.csv"), EventsCsv(result.Events), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "phylogeny.csv"), PhylogenyCsv(result.Phylogeny), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "energy.csv"), EnergyCsv(result.EnergyAudits), new UTF8Encoding(false));
        var summary = new
        {
            scenario,
            seed = settings.WorldSeed,
            years = settings.Years,
            initialPopulation = settings.PopulationSize,
            finalPopulation = result.FinalPopulation,
            livingSpecies = result.LivingSpecies,
            finiteMetrics = result.HasFiniteMetrics,
            phylogenyAcyclic = result.Phylogeny.IsAcyclic(),
            eventCount = result.Events.Count,
            speciationEvents = CountEvents(result, EvolutionEventKind.Speciation),
            extinctionEvents = CountEvents(result, EvolutionEventKind.Extinction),
            massDieOffEvents = CountEvents(result, EvolutionEventKind.MassDieOff),
            balancedEnergyYears = result.EnergyAudits.Count(audit => audit.IsBalanced(1e-4f)),
            energyAuditYears = result.EnergyAudits.Count
        };
        File.WriteAllText(Path.Combine(directory, "summary.json"),
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    private static string EventsCsv(IReadOnlyList<EvolutionEventRecord> events)
    {
        var text = new StringBuilder("kind,cause,year,tick,species_id,parent_species_id,region,count,trait_delta\n");
        for (int i = 0; i < events.Count; i++)
        {
            EvolutionEventRecord value = events[i];
            text.Append(value.Kind).Append(',').Append(value.Cause).Append(',')
                .Append(value.Year.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(value.Tick.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(value.SpeciesId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(value.ParentSpeciesId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(value.Region.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(value.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(TraitCsv(value.TraitDelta)).Append('\n');
        }
        return text.ToString();
    }

    private static string PhylogenyCsv(SpeciesPhylogeny phylogeny)
    {
        var text = new StringBuilder("record,species_id,parent_species_id,kingdom,tick,extinct_tick,extinct,cause\n");
        foreach (SpeciesNode node in phylogeny.Nodes)
        {
            text.Append("species,").Append(node.SpeciesId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(node.ParentSpeciesId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(node.Kingdom).Append(',').Append(node.FoundedAtTick.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(node.ExtinctAtTick.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(node.IsExtinct ? "true" : "false").Append(',').Append(node.OriginCause).Append('\n');
        }
        foreach (PhylogenyEdge edge in phylogeny.Edges)
        {
            text.Append("edge,").Append(edge.ChildSpeciesId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(edge.ParentSpeciesId.ToString(CultureInfo.InvariantCulture))
                .Append(",,").Append(edge.Tick.ToString(CultureInfo.InvariantCulture)).Append(",,,")
                .Append(edge.Cause).Append('\n');
        }
        return text.ToString();
    }

    private static string EnergyCsv(IReadOnlyList<EnergyFlowAudit> audits)
    {
        var text = new StringBuilder("year,producer_npp,producer_respiration,herbivore_intake,producer_stock_delta,producer_detritus,herbivore_respiration,predator_intake,herbivore_stock_delta,herbivore_detritus,predator_respiration,predator_stock_delta,predator_detritus,fertility_conversion,detritus_stock_delta,ecosystem_export,residual\n");
        for (int i = 0; i < audits.Count; i++)
        {
            EnergyFlowAudit audit = audits[i];
            text.Append(i + 1).Append(',');
            AppendFloat(text, audit.ProducerNpp); AppendFloat(text, audit.ProducerRespiration);
            AppendFloat(text, audit.HerbivoreIntake); AppendFloat(text, audit.ProducerStockDelta);
            AppendFloat(text, audit.ProducerDetritus); AppendFloat(text, audit.HerbivoreRespiration);
            AppendFloat(text, audit.PredatorIntake); AppendFloat(text, audit.HerbivoreStockDelta);
            AppendFloat(text, audit.HerbivoreDetritus); AppendFloat(text, audit.PredatorRespiration);
            AppendFloat(text, audit.PredatorStockDelta); AppendFloat(text, audit.PredatorDetritus);
            AppendFloat(text, audit.FertilityConversion); AppendFloat(text, audit.DetritusStockDelta);
            AppendFloat(text, audit.EcosystemExport);
            text.Append(audit.EcosystemResidual.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }
        return text.ToString();
    }

    private static void AppendFloat(StringBuilder text, float value)
        => text.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(',');

    private static string TraitCsv(EvolutionTraitVector value)
    {
        var parts = new string[EvolutionTraitVector.ComponentCount];
        for (int i = 0; i < parts.Length; i++)
            parts[i] = value.Get(i).ToString("R", CultureInfo.InvariantCulture);
        return string.Join("|", parts);
    }

    private static int CountEvents(HeadlessEvolutionResult result, EvolutionEventKind kind)
    {
        int count = 0;
        for (int i = 0; i < result.Events.Count; i++) if (result.Events[i].Kind == kind) count++;
        return count;
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--help" || arg == "-h") { result["help"] = "true"; continue; }
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Unexpected positional argument: " + arg);
            string key = arg.Substring(2);
            if (key == "all") { result["scenario"] = "all"; continue; }
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Missing value for " + arg);
            result[key] = args[++i];
        }
        return result;
    }

    private static string Get(Dictionary<string, string> options, string key, string fallback)
        => options.TryGetValue(key, out string value) ? value : fallback;

    private static int ParseInt(Dictionary<string, string> options, string key, int fallback)
        => options.TryGetValue(key, out string value)
            ? int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) : fallback;

    private static ulong ParseULong(Dictionary<string, string> options, string key, ulong fallback)
        => options.TryGetValue(key, out string value)
            ? ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) : fallback;

    private static (string Name, EvolutionScenarioKind Kind) FindScenario(string name)
    {
        for (int i = 0; i < Scenarios.Length; i++) if (Scenarios[i].Name == name) return Scenarios[i];
        throw new ArgumentException("Unknown scenario '" + name + "'. Supported: " +
            string.Join(", ", Scenarios.Select(scenario => scenario.Name)) + ", all.");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("LifeIsEndless deterministic evolution harness");
        Console.WriteLine("Usage: dotnet run --project Tools/EvolutionCli -- [options]");
        Console.WriteLine("  --scenario default|ice-age|predator-pressure|drought|mass-extinction-recovery|all");
        Console.WriteLine("  --seed <ulong> --years <int> --population <int> --regions <int>");
        Console.WriteLine("  --kingdom animal|plant --out <directory> --help");
        Console.WriteLine("Writes metrics.csv, events.csv, phylogeny.csv, energy.csv and summary.json.");
    }
}
