# RCT-Source
This is the backend console application that drives the Roblox-Client-Tracker repository on GitHub!

# Setup
For this to run correctly, you need a Git CLI for the program to communicate with.<br/>
If you happen to not have that, you can find one here: https://git-scm.com/

You'll need to fork these projects into the parent directory of this repository:<br/>
(Make sure you get them building with their NuGet packages fetched!)

- https://github.com/CloneTrooper1019/Roblox-Studio-Mod-Manager
- https://github.com/CloneTrooper1019/Roblox-API-Dump-Tool
- https://github.com/CloneTrooper1019/Roblox-File-Format

The application authenticates with GitHub using an SSH key.<br/>
It is expected to be located at `~/.ssh/RobloxClientTracker`

The generated SSH key needs to be connected to the GitHub account that will push changes. See here for assistance:
https://docs.github.com/en/github/authenticating-to-github/adding-a-new-ssh-key-to-your-github-account

In the settings of the client tracker project, you'll need to configure the following options:

- ClientRepoName: The repository name to pull and push to, when in the client tracker mode. 
- FFlagRepoName: The repository name to push and pull to, when in the fast flag tracker mode.
- RepoOwner: The name of the account/organization who owns both of the above repository names.
- BotName: The name used by the account authenticated with .ssh to commit to both repositories.
- BotEmail: The email used by the account authenticated with .ssh to commit to both repositories.

There should be a `roblox`, `sitetest1.robloxlabs` and `sitetest2.robloxlabs` branch setup in the repository assugned to ClientRepoName.
Each successive branch should be derived from the predecessor.

You may also need to enable long file paths on Windows if you don't have them enabled already.
Roblox's package dependencies have proven to be deeply nested at times.

1. Open `regedit.exe`
2. Navigate to `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem`
3. If it doesn't exist already, create a REG_DWORD named `LongPathsEnabled`
4. Set the value of `LongPathsEnabled` to 1 if it hasn't been already.
5. Restart your computer if it wasn't set already.
6. Open `cmd.exe` in administrator mode
7. Run: `git config --system core.longpaths true`

Lastly, set the build option in Visual Studio Release (x64) and build the application.
The shortcuts in the `stage` folder should now hopefully be functional. If not, check their absolute paths and make sure they point to the built exe.

- Production (roblox)
- Trunk (sitetest1.robloxlabs)
- Integration (sitetest2.robloxlabs)
- FastFlags (clientsettings.roblox.com)

# Launch Options

`-branch [domain.name]`<br/>
The web domain branch of Roblox that will be built on.

`-parent [domain.name]`<br/>
The parent domain branch of `-branch`

`-trackMode [Client, FastFlags]`<br/>
The runtime operation mode of the tracker.

`-manualBuild`<br/>
Attempts to analyze a manually assembled branch folder placed in the working stage directory.

`-forceRebase`<br/>
Forces git to attempt a merge of the branch with its parent repository.

`-forceUpdate`<br/>
Forces the client tracker to analyze the current build as a new update.

`-forceCommit`<br/>
Forces git to commit whatever changes have been stashed when updating.

`-verboseGitLogs`<br/>
Forces git to log non-error messages to the console.

`-updateFrequency #`<br/>
Sets the frequency (in minutes) that updates are checked for.

`-forceVersionId 0.0.0.0`<br/>
Forces the client to return the provided version id when fetching the latest version.

`-forceVersionGuid version-0123456789abcdef`<br/>
Forces the client to return the provided version guid when fetching the latest version.
