# Installing AoE3 Mod Launcher

## Quick start

1. Download `Aoe3ModLauncher.exe` from the latest [GitHub release](https://github.com/YOUR-REPO/releases/latest).
2. (Optional but recommended) Verify the SHA-256 hash matches the one published in the release notes:
   ```powershell
   Get-FileHash Aoe3ModLauncher.exe -Algorithm SHA256
   ```
3. Double-click `Aoe3ModLauncher.exe`.

That's it — no installer, no .NET runtime needed, no extra files. The launcher is a single self-contained .exe.

---

## If Windows blocks the launcher

This is **expected** for a self-published .exe without an expensive code-signing certificate. The launcher is safe; Windows just doesn't recognise the publisher yet. Pick the path that matches the warning you see:

### "Windows protected your PC" (SmartScreen)

A blue dialog with *"Microsoft Defender SmartScreen prevented an unrecognized app from starting."*

→ Click **More info** (small link, easy to miss) → **Run anyway**.

You only need to do this the first time.

### "This app has been blocked by your system administrator" (Smart App Control)

This appears on Windows 11 PCs that have **Smart App Control** enabled. Smart App Control is stricter than SmartScreen — there's no "Run anyway" button.

You have three options:

**Option A — Right-click → Properties → Unblock**

1. Right-click `Aoe3ModLauncher.exe` → **Properties**.
2. At the bottom of the *General* tab, look for a **Security** notice with an *Unblock* checkbox.
3. Tick **Unblock**, click **OK**, and try launching again.

**Option B — Turn off Smart App Control temporarily**

1. Open **Windows Security** → **App & browser control** → **Smart App Control settings**.
2. Set Smart App Control to **Off**.
3. ⚠️ Smart App Control can only be re-enabled by reinstalling Windows. If you turn it off, it stays off.

**Option C — Report the file to Microsoft**

If many users hit the same block, submitting the .exe to Microsoft for analysis builds reputation in their cloud and Smart App Control eventually trusts it.

1. Go to https://www.microsoft.com/en-us/wdsi/filesubmission
2. Sign in with a Microsoft account.
3. Choose **Software developer** as your role.
4. Upload `Aoe3ModLauncher.exe` and explain that Smart App Control is blocking a legitimate open-source launcher.
5. Microsoft typically responds in 24–72 hours.

---

## Why does this happen?

The launcher is open-source and built from the public source tree. Each release is signed with a self-signed certificate (`CN=Gorgorito`) so you can verify the binary hasn't been tampered with by checking the digital signature in the file's properties.

What it doesn't have is a **commercial code-signing certificate** from a Certificate Authority like DigiCert or Sectigo — those cost $200–700/year per certificate and aren't really feasible for a free, open-source modding tool. Without one, Windows treats the publisher as "unknown" and shows the warnings above the first time you run a new release.

If you want to verify the launcher is exactly what was built from this repository, every release ships with a SHA-256 hash. Compare the hash of your downloaded file (`Get-FileHash Aoe3ModLauncher.exe -Algorithm SHA256`) against the one in the release notes — if they match, the binary is authentic.
