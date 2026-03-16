package cmd

import (
	"fmt"

	"github.com/youngwoocho02/unity-cli/internal/client"
)

func profilerCmd(args []string, send sendFn) (*client.CommandResponse, error) {
	if len(args) == 0 {
		args = []string{"hierarchy"}
	}

	action := args[0]
	flags := parseSubFlags(args[1:])

	params := map[string]interface{}{"action": action}

	switch action {
	case "hierarchy":
		setInt(flags, params, "parent", "parent_id")
		setInt(flags, params, "frame", "frame")
		setInt(flags, params, "frames", "frames")
		setInt(flags, params, "from", "from")
		setInt(flags, params, "to", "to")
		setInt(flags, params, "thread", "thread_index")
		setStr(flags, params, "root", "root")
		setFloat(flags, params, "min", "min_time")
		setStr(flags, params, "sort", "sort_by")
		setInt(flags, params, "max", "max_items")
		setInt(flags, params, "depth", "depth")
	case "enable", "disable", "status", "clear":
		// no extra params
	default:
		return nil, fmt.Errorf("unknown profiler action: %s\nAvailable: hierarchy, enable, disable, status, clear", action)
	}

	return send("manage_profiler", params)
}
