package cmd

import (
	"fmt"
	"strconv"
	"strings"

	"github.com/devchan97/unity-cli/internal/client"
)

func execCmd(args []string, send sendFn) (*client.CommandResponse, error) {
	if len(args) == 0 {
		return nil, fmt.Errorf("usage: unity-cli exec \"<C# code>\"")
	}

	code := args[0]
	flags := parseSubFlags(args[1:])

	params := map[string]interface{}{"code": code}

	if usings, ok := flags["usings"]; ok {
		params["usings"] = strings.Split(usings, ",")
	}

	if t, ok := flags["exec-timeout"]; ok {
		if n, err := strconv.Atoi(t); err == nil {
			params["timeout"] = n
		}
	}

	return send("execute_csharp", params)
}
