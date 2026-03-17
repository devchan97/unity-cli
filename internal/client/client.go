package client

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// Debug enables verbose HTTP request/response logging to stderr.
var Debug bool

var defaultClient = &http.Client{
	Transport: &http.Transport{
		MaxIdleConns:        5,
		MaxIdleConnsPerHost: 5,
		IdleConnTimeout:     30 * time.Second,
	},
}

func debugLog(format string, args ...interface{}) {
	if !Debug {
		return
	}
	fmt.Fprintf(os.Stderr, "[DEBUG] "+format+"\n", args...)
}

type Instance struct {
	ProjectPath  string `json:"projectPath"`
	Port         int    `json:"port"`
	PID          int    `json:"pid"`
	UnityVersion string `json:"unityVersion,omitempty"`
	RegisteredAt string `json:"registeredAt,omitempty"`
}

type CommandRequest struct {
	Command string      `json:"command"`
	Params  interface{} `json:"params"`
}

type CommandResponse struct {
	Success bool            `json:"success"`
	Message string          `json:"message"`
	Data    json.RawMessage `json:"data,omitempty"`
}

func instancesPath() string {
	home, _ := os.UserHomeDir()
	return filepath.Join(home, ".unity-cli", "instances.json")
}

func DiscoverInstance(project string, port int) (*Instance, error) {
	if port > 0 {
		return &Instance{ProjectPath: "override", Port: port}, nil
	}

	path := instancesPath()
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, fmt.Errorf("no Unity instances found.\nIs Unity running with the Connector package?\nExpected: %s", path)
	}

	var instances []Instance
	if err := json.Unmarshal(data, &instances); err != nil {
		return nil, fmt.Errorf("failed to parse instances.json: %w", err)
	}

	if len(instances) == 0 {
		return nil, fmt.Errorf("no Unity instances registered")
	}

	if project != "" {
		for _, inst := range instances {
			if strings.Contains(inst.ProjectPath, project) {
				return &inst, nil
			}
		}
		return nil, fmt.Errorf("no Unity instance found for project: %s", project)
	}

	return &instances[len(instances)-1], nil
}

func Send(inst *Instance, command string, params interface{}, timeoutMs int) (*CommandResponse, error) {
	if params == nil {
		params = map[string]interface{}{}
	}

	body, err := json.Marshal(CommandRequest{Command: command, Params: params})
	if err != nil {
		return nil, err
	}

	url := fmt.Sprintf("http://127.0.0.1:%d/command", inst.Port)

	debugLog("-> POST %s", url)
	debugLog("-> Body: %s", string(body))

	ctx, cancel := context.WithTimeout(context.Background(), time.Duration(timeoutMs)*time.Millisecond)
	defer cancel()

	req, err := http.NewRequestWithContext(ctx, "POST", url, bytes.NewReader(body))
	if err != nil {
		return nil, err
	}
	req.Header.Set("Content-Type", "application/json")

	resp, err := defaultClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("cannot connect to Unity at port %d: %v", inst.Port, err)
	}
	defer resp.Body.Close()

	debugLog("<- Status: %d", resp.StatusCode)

	if resp.StatusCode != http.StatusOK {
		var errBody []byte
		errBody, _ = io.ReadAll(resp.Body)
		debugLog("<- Body: %s", string(errBody))
		if len(errBody) > 0 {
			return nil, fmt.Errorf("HTTP %d from Unity: %s", resp.StatusCode, string(errBody))
		}
		return nil, fmt.Errorf("HTTP %d from Unity (command: %s)", resp.StatusCode, command)
	}

	respBody, err := io.ReadAll(resp.Body)
	if err != nil || len(respBody) == 0 {
		debugLog("<- Body: (empty)")
		return &CommandResponse{
			Success: true,
			Message: fmt.Sprintf("%s sent (connection closed before response)", command),
		}, nil
	}

	debugLog("<- Body: %s", string(respBody))

	var result CommandResponse
	if err := json.Unmarshal(respBody, &result); err != nil {
		return &CommandResponse{
			Success: true,
			Message: string(respBody),
		}, nil
	}

	return &result, nil
}

type BatchRequest struct {
	Commands []map[string]interface{} `json:"commands"`
}

func SendBatch(inst *Instance, commands []map[string]interface{}, timeoutMs int) (*CommandResponse, error) {
	body, err := json.Marshal(BatchRequest{Commands: commands})
	if err != nil {
		return nil, err
	}

	url := fmt.Sprintf("http://127.0.0.1:%d/batch", inst.Port)

	debugLog("-> POST %s", url)
	debugLog("-> Body: %s", string(body))

	ctx, cancel := context.WithTimeout(context.Background(), time.Duration(timeoutMs)*time.Millisecond)
	defer cancel()

	req, err := http.NewRequestWithContext(ctx, "POST", url, bytes.NewReader(body))
	if err != nil {
		return nil, err
	}
	req.Header.Set("Content-Type", "application/json")

	resp, err := defaultClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("cannot connect to Unity at port %d: %v", inst.Port, err)
	}
	defer resp.Body.Close()

	debugLog("<- Status: %d", resp.StatusCode)

	if resp.StatusCode != http.StatusOK {
		respBody, _ := io.ReadAll(resp.Body)
		debugLog("<- Body: %s", string(respBody))
		if len(respBody) > 0 {
			return nil, fmt.Errorf("HTTP %d from Unity: %s", resp.StatusCode, string(respBody))
		}
		return nil, fmt.Errorf("HTTP %d from Unity (batch)", resp.StatusCode)
	}

	respBody, err := io.ReadAll(resp.Body)
	if err != nil || len(respBody) == 0 {
		debugLog("<- Body: (empty)")
		return &CommandResponse{
			Success: true,
			Message: "batch sent (connection closed before response)",
		}, nil
	}

	debugLog("<- Body: %s", string(respBody))

	var result CommandResponse
	if err := json.Unmarshal(respBody, &result); err != nil {
		return &CommandResponse{
			Success: true,
			Message: string(respBody),
		}, nil
	}

	return &result, nil
}
