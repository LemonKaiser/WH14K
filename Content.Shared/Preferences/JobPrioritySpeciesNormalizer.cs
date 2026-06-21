using System.Linq;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared.Preferences;

public static class JobPrioritySpeciesNormalizer
{
    public static readonly ProtoId<JobPrototype> SpeciesPreviewFallbackJob = "WH40KSpeciesPreview";

    public static HumanoidCharacterProfile EnsureSpeciesCompatibleJobPriorities(
        HumanoidCharacterProfile profile,
        IPrototypeManager prototypeManager,
        SharedRoleSystem roleSystem,
        bool preferFirstAvailable = false)
    {
        var selectedJob = ResolveSelectedJob(profile, prototypeManager, roleSystem, preferFirstAvailable);
        var priorities = new Dictionary<ProtoId<JobPrototype>, JobPriority>();

        foreach (var (jobId, priority) in profile.JobPriorities)
        {
            if (priority == JobPriority.Never ||
                jobId == SpeciesPreviewFallbackJob ||
                !prototypeManager.TryIndex(jobId, out JobPrototype? job) ||
                !IsSpeciesAllowedForJob(profile.Species, job, roleSystem))
            {
                continue;
            }

            priorities[jobId] = priority == JobPriority.High
                ? JobPriority.Medium
                : priority;
        }

        priorities[selectedJob] = JobPriority.High;
        return profile.WithJobPriorities(priorities);
    }

    public static bool IsSpeciesAllowedForJob(
        ProtoId<SpeciesPrototype> speciesId,
        JobPrototype job,
        SharedRoleSystem roleSystem)
    {
        var requirements = roleSystem.GetRoleRequirements(job);
        if (requirements == null)
            return true;

        foreach (var requirement in requirements)
        {
            if (requirement is not SpeciesRequirement speciesRequirement)
                continue;

            var listed = speciesRequirement.Species.Contains(speciesId);
            if (speciesRequirement.Inverted ? listed : !listed)
                return false;
        }

        return true;
    }

    private static ProtoId<JobPrototype> ResolveSelectedJob(
        HumanoidCharacterProfile profile,
        IPrototypeManager prototypeManager,
        SharedRoleSystem roleSystem,
        bool preferFirstAvailable)
    {
        var currentHigh = profile.JobPriorities.FirstOrDefault(p => p.Value == JobPriority.High).Key;
        if (!preferFirstAvailable &&
            currentHigh.Id != null &&
            prototypeManager.TryIndex(currentHigh, out JobPrototype? currentJob) &&
            IsSpeciesAllowedForJob(profile.Species, currentJob, roleSystem))
        {
            return currentHigh;
        }

        if (!preferFirstAvailable &&
            prototypeManager.TryIndex(SharedGameTicker.FallbackOverflowJob, out JobPrototype? fallbackOverflow) &&
            IsSpeciesAllowedForJob(profile.Species, fallbackOverflow, roleSystem))
        {
            return SharedGameTicker.FallbackOverflowJob;
        }

        if (TryGetFirstAvailableJobForSpecies(profile.Species, prototypeManager, roleSystem, out var firstJob))
            return firstJob;

        return SpeciesPreviewFallbackJob;
    }

    private static bool TryGetFirstAvailableJobForSpecies(
        ProtoId<SpeciesPrototype> speciesId,
        IPrototypeManager prototypeManager,
        SharedRoleSystem roleSystem,
        out ProtoId<JobPrototype> jobId)
    {
        var departments = prototypeManager.EnumeratePrototypes<DepartmentPrototype>()
            .Where(department => !department.EditorHidden)
            .ToList();
        departments.Sort(DepartmentUIComparer.Instance);

        foreach (var department in departments)
        {
            var jobs = department.Roles
                .Select(id => prototypeManager.TryIndex(id, out JobPrototype? job) ? job : null)
                .Where(job => job is { SetPreference: true })
                .Cast<JobPrototype>()
                .ToArray();

            Array.Sort(jobs, JobUIComparer.Instance);

            foreach (var job in jobs)
            {
                if (!IsSpeciesAllowedForJob(speciesId, job, roleSystem))
                    continue;

                jobId = job.ID;
                return true;
            }
        }

        jobId = default;
        return false;
    }
}
