package cmd

import (
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"regexp"
	"runtime"
	"strings"
)

const repoAPI = "https://api.github.com/repos/devchan97/unity-cli/releases/latest"

type ghRelease struct {
	TagName string    `json:"tag_name"`
	Assets  []ghAsset `json:"assets"`
}

type ghAsset struct {
	Name               string `json:"name"`
	BrowserDownloadURL string `json:"browser_download_url"`
}

func updateCmd(args []string) error {
	flags := parseSubFlags(args)
	_, checkOnly := flags["check"]
	_, connectorOnly := flags["connector"]

	if connectorOnly {
		return updateConnector(checkOnly)
	}

	fmt.Println("Checking for updates...")

	release, err := fetchLatestRelease()
	if err != nil {
		return fmt.Errorf("failed to check for updates: %w", err)
	}

	latest := release.TagName
	current := Version

	if current == latest {
		fmt.Printf("Already up to date (%s)\n", current)
		return nil
	}

	fmt.Printf("Update available: %s → %s\n", current, latest)

	if checkOnly {
		return nil
	}

	asset := findAsset(release.Assets)
	if asset == nil {
		return fmt.Errorf("no binary found for %s/%s", runtime.GOOS, runtime.GOARCH)
	}

	exe, err := os.Executable()
	if err != nil {
		return fmt.Errorf("cannot locate current binary: %w", err)
	}
	exe, err = filepath.EvalSymlinks(exe)
	if err != nil {
		return fmt.Errorf("cannot resolve binary path: %w", err)
	}

	fmt.Printf("Downloading %s...\n", asset.Name)

	tmpFile, err := download(asset.BrowserDownloadURL, filepath.Dir(exe))
	if err != nil {
		return fmt.Errorf("download failed: %w", err)
	}
	defer os.Remove(tmpFile)

	if err := os.Chmod(tmpFile, 0755); err != nil {
		return fmt.Errorf("chmod failed: %w", err)
	}

	backup := exe + ".bak"
	if err := os.Rename(exe, backup); err != nil {
		return fmt.Errorf("backup failed: %w", err)
	}

	if err := os.Rename(tmpFile, exe); err != nil {
		if restoreErr := os.Rename(backup, exe); restoreErr != nil {
			return fmt.Errorf("replace failed: %w (restore also failed: %v)", err, restoreErr)
		}
		return fmt.Errorf("replace failed: %w", err)
	}

	_ = os.Remove(backup)

	fmt.Printf("Updated to %s\n", latest)
	return nil
}

func fetchLatestRelease() (*ghRelease, error) {
	resp, err := http.Get(repoAPI)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("GitHub API returned %d", resp.StatusCode)
	}

	var release ghRelease
	if err := json.NewDecoder(resp.Body).Decode(&release); err != nil {
		return nil, err
	}
	return &release, nil
}

func findAsset(assets []ghAsset) *ghAsset {
	suffix := fmt.Sprintf("%s-%s", runtime.GOOS, runtime.GOARCH)
	for i, a := range assets {
		if strings.Contains(a.Name, suffix) {
			return &assets[i]
		}
	}
	return nil
}

const connectorPackage = "com.devchan97.unity-cli-connector"
const connectorGitURL = "https://github.com/devchan97/unity-cli.git?path=unity-connector"

func updateConnector(checkOnly bool) error {
	fmt.Println("Checking for connector updates...")

	release, err := fetchLatestRelease()
	if err != nil {
		return fmt.Errorf("failed to check for updates: %w", err)
	}
	latest := release.TagName

	// Find manifest.json — walk up from cwd looking for Assets/ sibling
	manifest, err := findManifest()
	if err != nil {
		return fmt.Errorf("manifest.json not found: %w\nRun from inside a Unity project directory", err)
	}

	data, err := os.ReadFile(manifest)
	if err != nil {
		return fmt.Errorf("cannot read manifest.json: %w", err)
	}
	content := string(data)

	// Match current connector URL (with or without #tag)
	re := regexp.MustCompile(`("` + regexp.QuoteMeta(connectorPackage) + `"\s*:\s*"` + regexp.QuoteMeta(connectorGitURL) + `)(?:#[^"]*)?(")\s*`)
	m := re.FindStringIndex(content)
	if m == nil {
		return fmt.Errorf("connector package not found in manifest.json")
	}

	// Extract current tag
	tagRe := regexp.MustCompile(regexp.QuoteMeta(connectorGitURL) + `(?:#([^"]+))?`)
	tagM := tagRe.FindStringSubmatch(content)
	current := "(no tag)"
	if len(tagM) > 1 && tagM[1] != "" {
		current = tagM[1]
	}

	if current == latest {
		fmt.Printf("Connector already up to date (%s)\n", latest)
		return nil
	}

	fmt.Printf("Connector update available: %s → %s\n", current, latest)
	if checkOnly {
		return nil
	}

	// Replace URL with pinned tag
	newURL := connectorGitURL + "#" + latest
	newContent := tagRe.ReplaceAllLiteralString(content, newURL)

	if err := os.WriteFile(manifest, []byte(newContent), 0644); err != nil {
		return fmt.Errorf("cannot write manifest.json: %w", err)
	}

	fmt.Printf("Updated manifest.json: connector pinned to %s\n", latest)
	fmt.Println("Reopen Unity or trigger Package Manager refresh to apply.")
	return nil
}

func findManifest() (string, error) {
	cwd, err := os.Getwd()
	if err != nil {
		return "", err
	}
	// Walk up to 4 levels looking for Packages/manifest.json
	dir := cwd
	for i := 0; i < 5; i++ {
		candidate := filepath.Join(dir, "Packages", "manifest.json")
		if _, err := os.Stat(candidate); err == nil {
			return candidate, nil
		}
		parent := filepath.Dir(dir)
		if parent == dir {
			break
		}
		dir = parent
	}
	return "", fmt.Errorf("not found from %s", cwd)
}

func download(url string, targetDir string) (string, error) {
	resp, err := http.Get(url)
	if err != nil {
		return "", err
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("download returned %d", resp.StatusCode)
	}

	tmp, err := os.CreateTemp(targetDir, "unity-cli-update-*")
	if err != nil {
		return "", err
	}
	defer tmp.Close()

	if _, err := io.Copy(tmp, resp.Body); err != nil {
		os.Remove(tmp.Name())
		return "", err
	}

	return tmp.Name(), nil
}
