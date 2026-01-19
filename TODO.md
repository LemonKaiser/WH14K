# Plan to Fix Unknown JobPrototype 'FirstAssistant' Error

## Problem Analysis
- Error: Robust.Shared.Prototypes.UnknownPrototypeException: Unknown JobPrototype prototype: FirstAssistant
- Occurs in DepartmentTimeRequirement.cs when trying to index the job prototype for time requirements.
- FirstAssistant is referenced in departments.yml but the JobPrototype was not defined.

## Steps Completed
- [x] Created JobPrototype for FirstAssistant in Resources/Prototypes/_WH40K/Roles/Jobs/Command/first_assistant.yml
- [x] Added English localization for job-name-first-assistant in Resources/Locale/en-US/job/job-names.ftl
- [x] Added English localization for job-description-first-assistant in Resources/Locale/en-US/job/job-names.ftl
- [x] Added Russian localization for job-description-first-assistant in Resources/Locale/ru-RU/_wh40k/job/job-description.ftl
- [x] Updated job description reference to job-description-first-assistant

## Verification Steps
- [ ] Run the server and attempt to join a game to check if the error is resolved.
- [ ] Ensure FirstAssistant appears in job selection and spawn points work.
- [ ] Check that department time requirements work for departments including FirstAssistant.

## Additional Notes
- FirstAssistant is listed in Personnel and Governance departments.
- PlayTimeTracker JobFirstAssistant already exists.
- JobIconFirstAssistant already exists.
- SpawnPointFirstAssistant already exists.
