package cmd

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"time"

	"github.com/devchan97/unity-cli/internal/client"
)

type UnityStatus struct {
	State        string `json:"state"`
	ProjectPath  string `json:"projectPath"`
	Port         int    `json:"port"`
	PID          int    `json:"pid"`
	UnityVersion string `json:"unityVersion"`
	Timestamp    int64  `json:"timestamp"`
}

func statusCmd(inst *client.Instance) error {
	status, err := readStatus(inst.Port)
	if err != nil {
		return fmt.Errorf("no status for port %d — Unity may not be running", inst.Port)
	}

	age := time.Since(time.UnixMilli(status.Timestamp))
	if age > 3*time.Second {
		fmt.Fprintf(os.Stderr, "Unity (port %d): not responding (last heartbeat %s ago)\n", status.Port, age.Truncate(time.Second))
		return nil
	}

	fmt.Printf("Unity (port %d): %s\n", status.Port, status.State)
	fmt.Printf("  Project: %s\n", status.ProjectPath)
	fmt.Printf("  Version: %s\n", status.UnityVersion)
	fmt.Printf("  PID:     %d\n", status.PID)
	return nil
}

func readStatus(port int) (*UnityStatus, error) {
	home, _ := os.UserHomeDir()
	path := filepath.Join(home, ".unity-cli", "status", fmt.Sprintf("%d.json", port))

	data, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}

	var status UnityStatus
	if err := json.Unmarshal(data, &status); err != nil {
		return nil, err
	}
	return &status, nil
}

// waitForAlive reads the current timestamp, then polls until a newer one appears.
// It re-discovers the instance on each poll to handle port changes after domain reloads.
func waitForAlive(port int, timeoutMs int) error {
	baseline := time.Now().UnixMilli()
	if status, err := readStatus(port); err == nil {
		baseline = status.Timestamp
	}

	// Already fresh — check if timestamp was updated within the last second
	if time.Now().UnixMilli()-baseline < 1000 {
		return nil
	}

	fmt.Fprintf(os.Stderr, "Waiting for Unity...\n")

	deadline := time.Now().Add(time.Duration(timeoutMs) * time.Millisecond)
	for time.Now().Before(deadline) {
		time.Sleep(500 * time.Millisecond)

		// Re-discover in case port changed after a domain reload
		effectivePort := port
		if inst, err := client.DiscoverInstance(flagProject, flagPort); err == nil {
			effectivePort = inst.Port
		}

		status, err := readStatus(effectivePort)
		if err != nil {
			continue
		}
		if status.Timestamp > baseline {
			fmt.Fprintf(os.Stderr, "Unity is ready.\n")
			return nil
		}
	}

	return fmt.Errorf("timed out waiting for Unity (port %d)", port)
}

// waitForReady polls until the heartbeat state becomes "ready", re-discovering
// the port on each tick to handle domain reloads that change the port.
func waitForReady(port int) error {
	fmt.Fprintf(os.Stderr, "Waiting for compilation...\n")

	deadline := time.Now().Add(120 * time.Second)
	for time.Now().Before(deadline) {
		time.Sleep(500 * time.Millisecond)

		// Re-discover in case port changed after a domain reload
		effectivePort := port
		if inst, err := client.DiscoverInstance(flagProject, flagPort); err == nil {
			effectivePort = inst.Port
		}

		status, err := readStatus(effectivePort)
		if err != nil {
			continue
		}
		if status.State == "ready" {
			fmt.Fprintf(os.Stderr, "Compilation complete.\n")
			return nil
		}
	}

	return fmt.Errorf("timed out waiting for compilation (port %d) — check Unity console for errors", port)
}
