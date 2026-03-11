package cmd

import (
	"fmt"
	"strconv"

	"github.com/youngwoocho02/unity-cli/internal/client"
)

func profilerCmd(args []string, send sendFn) (*client.CommandResponse, error) {
	if len(args) == 0 {
		args = []string{"hierarchy"}
	}

	action := args[0]
	flags := parseSubFlags(args[1:])

	switch action {
	case "hierarchy":
		params := map[string]interface{}{}
		if v, ok := flags["parent"]; ok {
			if n, err := strconv.Atoi(v); err == nil {
				params["parent_id"] = n
			}
		}
		if v, ok := flags["frame"]; ok {
			if n, err := strconv.Atoi(v); err == nil {
				params["frame"] = n
			}
		}
		if v, ok := flags["thread"]; ok {
			if n, err := strconv.Atoi(v); err == nil {
				params["thread_index"] = n
			}
		}
		if v, ok := flags["min"]; ok {
			if f, err := strconv.ParseFloat(v, 32); err == nil {
				params["min_time"] = f
			}
		}
		if v, ok := flags["sort"]; ok {
			params["sort_by"] = v
		}
		if v, ok := flags["max"]; ok {
			if n, err := strconv.Atoi(v); err == nil {
				params["max_items"] = n
			}
		}
		return send("profiler_hierarchy", params)

	case "enable":
		return send("manage_profiler", map[string]interface{}{"action": "enable"})

	case "disable":
		return send("manage_profiler", map[string]interface{}{"action": "disable"})

	case "status":
		return send("manage_profiler", map[string]interface{}{"action": "status"})

	case "clear":
		return send("manage_profiler", map[string]interface{}{"action": "clear"})

	default:
		return nil, fmt.Errorf("unknown profiler action: %s\nAvailable: hierarchy, enable, disable, status, clear", action)
	}
}
