# Reference repositories

All paths below are read-only code vaults for this task.

| Repository | Local path | Branch / HEAD | Remote evidence | Working-tree state at audit |
|---|---|---|---|---|
| New target | `D:\dev\guideXOS_NET10` | `main` / unborn | `origin = git@github.com:guideX/guideXOS_NET10.git` | Empty before this audit |
| Legacy C# | `D:\dev\guideXOS` | `main` / `4ff1ac4026deeb0799a6b66df3d9998a2438d46b` | `github = git@github.com:guideX/guideXOS-Legacy.git`; `origin = https://gitlab.com/guideX/guideXOS` | Clean at inspection |
| UEFI C# | `D:\dev\guideXOSUEFI` | `main` / `65178e81d8eb06ffc146185f7dd440a0f00c89b9` | `origin = git@github.com:guideX/guideXOS-Legacy-UEFI.git` | Clean at inspection |
| Server | `D:\dev\guideXOSServer` | `main` / `4933cd142781473c2317ed926124c088aa0cbbdc` | `origin = git@github.com:guideX/guideXOS-Server.git` | Dirty; changes were not touched |

## Target inventory

Before this audit, the target contained only `.git`. There was no solution, project, documentation, source, asset, or initial work. The local branch was an unborn `main` tracking `origin/main`; the remote-tracking branch was shown as gone because the remote exposed no heads in the read-only probe.

The SSH remote URL is syntactically the requested GitHub repository. A direct SSH `git ls-remote` was not usable because the local SSH client reported `Host key verification failed`. A direct HTTPS `git ls-remote` returned no heads and no repository-content evidence. Therefore:

- confirmed: the configured URL is `guideX/guideXOS_NET10`;
- confirmed: the local checkout is not populated from a remote commit;
- unconfirmed: authenticated SSH access and any future remote branch state.

## Read-only audit commands

The audit used `git status`, `git log`, `git remote -v`, `git rev-parse`, `git ls-remote` against the target only, `rg --files`, targeted `rg -n`, and read-only file/hash/diff inspection. No reference repository was staged, committed, pushed, reset, cleaned, or switched.

## Reference roles

- Legacy is the strongest source for the mature C# kernel/userland behavior, GRUB/Multiboot startup, filesystem breadth, desktop, applications, and assets.
- UEFI is the strongest source for the existing managed C# handoff experiment, UEFI memory-map/ELF/framebuffer work, `KMain` diagnostics, and post-`ExitBootServices` constraints.
- Server is the strongest source for a modern native kernel organization, a narrow explicit native app ABI, manifest/launch-target vocabulary, and a versioned `.gxapp` container concept.

