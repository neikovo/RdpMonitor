# Security policy

## Supported versions

Only the **latest release** receives security fixes: see [Releases](https://github.com/neikovo/RdpMonitor/releases/latest). Older versions are not patched; please update.

## Reporting a vulnerability

**Please do not open a public issue for a security problem.**

Use GitHub's private reporting: open the **Security** tab of this repository and choose **Report a vulnerability** (<https://github.com/neikovo/RdpMonitor/security/advisories/new>). Only the maintainer can see the report.

Please include:

- the version (see the agent/server file version, or the release you downloaded);
- what you found and why it matters (what an attacker could do);
- steps to reproduce, or a small proof of concept;
- your environment if it matters (Windows Server version, how the server and agents are installed).

Please do not include real passwords, tokens, or data from systems you do not own.

### What to expect

This is a small project maintained by one person, so there is no guaranteed response time. The aim is to acknowledge a report within about a week, to fix confirmed issues in a new release, and to say in the release notes what was fixed. If you want to be credited, say so in the report; if you prefer to stay anonymous, that is fine too.

Please give a reasonable time to fix the problem before you publish details.

## Scope

In scope: the code in this repository and the packages published on the Releases page (server, agent, install/update scripts).

Out of scope:

- Problems that require you to already be an **administrator** of the monitored machine or of the server. The tool does not protect against that: an administrator can stop the agent, clear logs or change the audit policy.
- Weaknesses in Windows, the mail server, your network or other software.
- A server or agent you left on the shipped placeholder settings (`CHANGE-ME…`) or exposed to the internet against the advice in [INSTALL.md](INSTALL.md).
- Findings that need physical access, or social engineering of the administrator.

## Using it safely

The full list, with the reasons, is in [INSTALL.md](INSTALL.md), section 10. In short:

- Set your own `Admin:Password` (12+ characters) and `Agent:EnrollmentToken` (16+ random characters); the server refuses the placeholders.
- Pin the server certificate on every agent (`Agent:ServerCertificateSha256`) instead of using `AllowInsecureTls`.
- Keep the server on an internal network or behind a VPN; do not expose port 5443 to the internet.
- Send alerts through a mail server you trust, with TLS if possible: they contain user names, IP addresses and command lines.
- Download only from the Releases page and check the file against `SHA256SUMS.txt`. The packages are **not code-signed**.
- Keep the program folders writable only by administrators (the installer does this unless you pass `-NoFolderLock`).

## Known limits

- The agent and the server run as `LocalSystem`.
- Detection depends on Windows auditing and the Security log; Group Policy can override the audit policy.
- Passwords on command lines are masked on a best-effort basis; not every way a program takes a secret can be recognised.
