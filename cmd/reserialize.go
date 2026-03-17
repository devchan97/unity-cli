package cmd

import (
	"github.com/devchan97/unity-cli/internal/client"
)

func reserializeCmd(args []string, send sendFn) (*client.CommandResponse, error) {
	if len(args) == 0 {
		return send("reserialize_assets", map[string]interface{}{})
	}
	if len(args) == 1 {
		return send("reserialize_assets", map[string]interface{}{"path": args[0]})
	}
	return send("reserialize_assets", map[string]interface{}{"paths": args})
}
