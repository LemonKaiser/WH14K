using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Dataset;
using Content.Shared.Random.Helpers;
using Robust.Shared.Random;
using Robust.Shared.Prototypes;
using Robust.Shared.Enums;
using Robust.Shared.Localization;

namespace Content.Shared.Humanoid
{
    /// <summary>
    /// Figure out how to name a humanoid with these extensions.
    /// </summary>
    public sealed class NamingSystem : EntitySystem
    {
        private static readonly ProtoId<SpeciesPrototype> FallbackSpecies = "Human";
        private static readonly ProtoId<LocalizedDatasetPrototype> FallbackHumanLastNames = "NamesLast";

        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly ILocalizationManager _loc = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

        public string GetName(string species, Gender? gender = null)
        {
            // if they have an old species or whatever just fall back to human I guess?
            // Some downstream is probably gonna have this eventually but then they can deal with fallbacks.
            if (!_prototypeManager.TryIndex(species, out SpeciesPrototype? speciesProto))
            {
                speciesProto = _prototypeManager.Index(FallbackSpecies);
                Log.Warning($"Unable to find species {species} for name, falling back to {FallbackSpecies}");
            }

            switch (speciesProto.Naming)
            {
                case SpeciesNaming.First:
                    return Loc.GetString("namepreset-first",
                        ("first", GetFirstName(speciesProto, gender)));
                case SpeciesNaming.TheFirstofLast:
                    return Loc.GetString("namepreset-thefirstoflast",
                        ("first", GetFirstName(speciesProto, gender)), ("last", GetLastName(speciesProto, gender)));
                case SpeciesNaming.FirstDashFirst:
                    return Loc.GetString("namepreset-firstdashfirst",
                        ("first1", GetFirstName(speciesProto, gender)), ("first2", GetFirstName(speciesProto, gender)));
                case SpeciesNaming.FirstLast:
                default:
                    return Loc.GetString("namepreset-firstlast",
                        ("first", GetFirstName(speciesProto, gender)), ("last", GetLastName(speciesProto, gender)));
            }
        }

        public string GetFirstName(SpeciesPrototype speciesProto, Gender? gender = null)
        {
            switch (gender)
            {
                case Gender.Male:
                    return PickDatasetValue(speciesProto.MaleFirstNames);
                case Gender.Female:
                    return PickDatasetValue(speciesProto.FemaleFirstNames);
                default:
                    if (_random.Prob(0.5f))
                        return PickDatasetValue(speciesProto.MaleFirstNames);
                    else
                        return PickDatasetValue(speciesProto.FemaleFirstNames);
            }
        }

        public string GetLastName(SpeciesPrototype speciesProto, Gender? gender = null)
        {
            if (speciesProto.ID == FallbackSpecies &&
                HumanoidNameScriptHelper.GetPreferredScript(_loc.DefaultCulture) == HumanoidNameScript.Latin &&
                _prototypeManager.TryIndex(FallbackHumanLastNames, out LocalizedDatasetPrototype? fallbackHumanLastNames))
            {
                return PickLocalizedDatasetValue(fallbackHumanLastNames);
            }

            switch (gender)
            {
                case Gender.Male:
                    return PickLastName(speciesProto.MaleLastNames);
                case Gender.Female:
                    return PickLastName(speciesProto.FemaleLastNames);
                default:
                    if (_random.Prob(0.5f))
                        return PickLastName(speciesProto.MaleLastNames);
                    else
                        return PickLastName(speciesProto.FemaleLastNames);
            }
        }

        private string PickLastName(string datasetId)
        {
            return PickDatasetValue(datasetId);
        }

        private string PickDatasetValue(string datasetId)
        {
            if (_prototypeManager.TryIndex<LocalizedDatasetPrototype>(datasetId, out var localizedDataset))
                return PickLocalizedDatasetValue(localizedDataset);

            if (_prototypeManager.TryIndex<DatasetPrototype>(datasetId, out var rawDataset))
                return _random.Pick(rawDataset.Values);

            throw new InvalidOperationException($"Unable to find name dataset {datasetId}.");
        }

        private string PickLocalizedDatasetValue(LocalizedDatasetPrototype dataset)
        {
            return Loc.GetString(_random.Pick(dataset.Values));
        }
    }
}
