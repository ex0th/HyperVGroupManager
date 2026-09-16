# Hyper-V VM Group Manager

A portable Windows desktop application (WPF, .NET 10) for managing **native Hyper-V VM groups**
(`VMCollectionType`) on standalone Hyper-V hosts and failover clusters.

The workflow is inspired by vSphere tags: a VM can belong to multiple groups, and memberships can
be added or removed without affecting its other group assignments. These groups are designed to be
used as stable VM selection objects in Veeam Backup & Replication. The application does not modify
Veeam configuration or backup jobs.

## Quick start

```powershell
dotnet build HyperVGroupManager.sln
dotnet test HyperVGroupManager.sln
dotnet run --project src\HyperVGroupManager.App
```

For more information, see the [usage guide](docs/usage.md),
[architecture documentation](docs/architecture.md), and
[PowerShell JSON contract](docs/powershell-json-contract.md).

## Project structure

* `src/HyperVGroupManager.App` — WPF application using MVVM and CommunityToolkit.Mvvm
* `src/HyperVGroupManager.Core` — models, interfaces, and domain logic without WPF or PowerShell dependencies
* `src/HyperVGroupManager.PowerShell` — PowerShell backend module for Windows PowerShell 5.1
* `tests/HyperVGroupManager.Tests` — xUnit tests; tests never execute real Hyper-V operations

## Safety principles

* The application **does not modify Veeam jobs** and does not access the Veeam API.
* It manages only native Hyper-V VM groups of type `VMCollectionType`.
* It does not use `Invoke-Expression` or dynamically concatenate PowerShell source code.
* Parameters are transferred exclusively through JSON files, never through command-line arguments.
* Change sets are validated in C# and again by the PowerShell module before execution.
* Every change set is bound to the connected target. Switching targets while changes are pending is blocked.
* SMTP passwords are never logged and are encrypted for the current Windows user with DPAPI.

## What's new in 0.2

* Native Fluent WPF interface with system light/dark mode and Windows accent colors
* Preview of the expected state, including all changes that have not yet been applied
* VMs can be assigned to newly planned groups within the same change run
* Groups can be deleted safely after all membership removals have been planned
* Complete preflight validation, backend response correlation, and a safety lock for uncertain server state
* Hardened PowerShell bootstrap with a command allowlist and input/output limits

## What's new in 0.3

* Integrated documentation through the Help button or `F1`
* Runtime language switching between German and English with a persistent per-user preference

## What's new in 0.3.1

* Consistent light/dark templates for buttons, text fields, password fields, and drop-downs
* Connection-aware action states and safe disabling of apply operations without pending changes
* Search placeholders and contextual empty-state messages
* Visual VM change markers with added, removed, and renamed group badges

## MVP scope

Included: native `VMCollectionType` groups on standalone hosts and clusters, multiple group
memberships, validated changes with an effective-state preview, JSON export, and scheduled email
reports for VMs without a group assignment.

Out of scope: Veeam API integration, `ManagementCollectionType`, nested groups, SCVMM,
Active Directory authentication, an installer, and automatic updates.

## License

This project is licensed under the [MIT License](LICENSE).
