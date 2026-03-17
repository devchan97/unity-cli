package cmd

import (
	"encoding/json"
	"fmt"
	"runtime"

	"github.com/devchan97/unity-cli/internal/client"
)

func versionCmd(args []string) error {
	flags := parseSubFlags(args)
	_, jsonOutput := flags["json"]

	if !jsonOutput {
		fmt.Println("unity-cli " + Version)
		return nil
	}

	// JSON mode: include CLI info + try to get connector info
	info := map[string]interface{}{
		"cli_version": Version,
		"go_version":  runtime.Version(),
		"os":          runtime.GOOS,
		"arch":        runtime.GOARCH,
	}

	// Try to connect to Unity for connector version
	inst, err := client.DiscoverInstance(flagProject, flagPort)
	if err == nil {
		resp, err := client.Send(inst, "manage_version", map[string]interface{}{}, 5000)
		if err == nil && resp.Success && resp.Data != nil {
			var connectorInfo map[string]interface{}
			if json.Unmarshal(resp.Data, &connectorInfo) == nil {
				info["connector"] = connectorInfo
			}
		}
	}

	b, _ := json.MarshalIndent(info, "", "  ")
	fmt.Println(string(b))
	return nil
}
