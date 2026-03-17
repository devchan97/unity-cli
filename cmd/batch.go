package cmd

import (
	"encoding/json"
	"fmt"

	"github.com/devchan97/unity-cli/internal/client"
)

func batchCmd(args []string, inst *client.Instance) (*client.CommandResponse, error) {
	if len(args) == 0 {
		return nil, fmt.Errorf("usage: unity-cli batch '<json-commands>'\nExample: unity-cli batch '[{\"command\":\"list_tools\",\"params\":{}}]'")
	}

	var commands []map[string]interface{}
	if err := json.Unmarshal([]byte(args[0]), &commands); err != nil {
		return nil, fmt.Errorf("invalid JSON: %w\nExpected: [{\"command\":\"...\",\"params\":{...}}, ...]", err)
	}

	return client.SendBatch(inst, commands, flagTimeout)
}
