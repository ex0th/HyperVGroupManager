# Microsoft Azure Artifact Signing setup

GitHub releases are signed with an Azure Artifact Signing **Public Trust** certificate profile.
Authentication uses GitHub OpenID Connect (OIDC), so the repository does not store a PFX, private
key, or Azure client secret.

> Public Trust is currently available to organizations in the EU and several other supported
> regions. Individual developers are currently limited to the United States and Canada. A German
> individual account therefore cannot obtain a Public Trust profile; a verified German company can.
> Check the current eligibility rules in the
> [Microsoft quickstart](https://learn.microsoft.com/azure/artifact-signing/quickstart).

## 1. Create the Azure signing resources

In the Azure portal:

1. Register the `Microsoft.CodeSigning` resource provider for the subscription if it is not already
   registered.
2. Create an **Artifact Signing Account**. Choose a supported region and note the account endpoint.
3. Assign yourself **Artifact Signing Identity Verifier** and at least **Contributor** where needed.
4. Under **Identity validations**, create and complete a **Public** organization validation. The
   legal organization details become part of the public signing identity. Validation can take
   several business days and must be completed in the Azure portal.
5. Create a certificate profile with type **Public Trust**. Do not choose **Public Trust Test**;
   test profiles are not publicly trusted.

Microsoft's full resource setup is documented in
[Set up Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/quickstart).

## 2. Create the GitHub OIDC identity

Create a Microsoft Entra application registration, for example
`HyperVGroupManager-GitHub-Releases`. A client secret is neither required nor wanted.

On the app registration, open **Certificates & secrets** > **Federated credentials** and add:

| Setting | Value |
| --- | --- |
| Scenario | GitHub Actions deploying Azure resources |
| Organization | `ex0th` |
| Repository | `HyperVGroupManager` |
| Entity type | Environment |
| Environment name | `release` |
| Credential name | `hypervgroupmanager-release` |

Using the environment subject is important because the release workflow accepts multiple version
tags while Entra federated credentials do not support wildcard tag subjects. The workflow job is
bound to the GitHub environment named `release`.

## 3. Grant only signing permission

Create a role assignment for the service principal belonging to the Entra application:

* Role: **Artifact Signing Certificate Profile Signer**
* Scope: the Public Trust certificate profile created above

Do not grant Contributor or Owner to the CI identity. The signer role at certificate-profile scope
is sufficient. See [Artifact Signing roles](https://learn.microsoft.com/azure/artifact-signing/tutorial-assign-roles).

## 4. Configure the GitHub environment

In GitHub, open **Settings** > **Environments**, create an environment called `release`, and
optionally require approval before deployment. Add these environment secrets:

| Secret | Azure value |
| --- | --- |
| `AZURE_CLIENT_ID` | Application (client) ID of the Entra app registration |
| `AZURE_TENANT_ID` | Directory (tenant) ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID containing the signing account |

Add these environment variables:

| Variable | Azure value |
| --- | --- |
| `ARTIFACT_SIGNING_ENDPOINT` | Region endpoint, for example `https://weu.codesigning.azure.net/` |
| `ARTIFACT_SIGNING_ACCOUNT_NAME` | Artifact Signing account name |
| `ARTIFACT_SIGNING_CERTIFICATE_PROFILE_NAME` | Public Trust certificate profile name |

The three identifiers stored as secrets are not private keys, but keeping them as environment
secrets follows the Azure GitHub OIDC guidance and keeps all release identity settings together.
Do not create `AZURE_CLIENT_SECRET`, `CODE_SIGNING_CERTIFICATE_BASE64`, or
`CODE_SIGNING_CERTIFICATE_PASSWORD`.

The endpoint must match the Azure region of the signing account. A region mismatch normally causes
a `403 Forbidden` signing error.

## 5. Publish and verify

Create a release with the existing script:

```powershell
.\scripts\New-Release.ps1
```

The workflow performs these security-relevant steps:

1. GitHub requests a short-lived OIDC token for the `release` environment.
2. `azure/login` exchanges it for a short-lived Azure access token.
3. Microsoft Artifact Signing signs the portable EXE with SHA-256 and an RFC 3161 timestamp.
4. The signed EXE is included in the MSI.
5. Microsoft Artifact Signing signs the MSI with the same profile.
6. Both Authenticode signatures are validated before ZIP, MSI, and checksums are published.

To inspect a downloaded release locally:

```powershell
Get-AuthenticodeSignature .\HyperVGroupManager.App.exe |
    Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate

Get-AuthenticodeSignature .\HyperVGroupManager-1.0.1-win-x64.msi |
    Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate
```

The expected status is `Valid`. New signing identities can still require time to build Microsoft
SmartScreen reputation; a valid public signature does not guarantee that SmartScreen warnings
disappear immediately.
