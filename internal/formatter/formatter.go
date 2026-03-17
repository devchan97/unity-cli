package formatter

import (
	"encoding/csv"
	"encoding/json"
	"fmt"
	"os"
	"sort"
	"strings"
)

// Format takes raw JSON data and a message, then formats the output
// based on the specified format type: "json" (default), "table", or "csv".
func Format(data json.RawMessage, message string, format string) string {
	// If no data, just return the message
	if data == nil || len(data) == 0 || string(data) == "null" {
		return message
	}

	// Try to unmarshal data
	var parsed interface{}
	if json.Unmarshal(data, &parsed) != nil {
		return string(data)
	}

	// If data is a plain string, always print raw (preserves tree output etc.)
	if s, ok := parsed.(string); ok {
		return s
	}

	switch format {
	case "table":
		if result, ok := formatTable(parsed); ok {
			return result
		}
		return formatJSON(parsed)
	case "csv":
		if result, ok := formatCSV(parsed); ok {
			return result
		}
		return formatJSON(parsed)
	default: // "json"
		return formatJSON(parsed)
	}
}

func formatJSON(v interface{}) string {
	b, err := json.MarshalIndent(v, "", "  ")
	if err != nil {
		return fmt.Sprintf("%v", v)
	}
	return string(b)
}

// formatTable attempts to render data as an aligned text table.
// Returns (result, true) if data is an array of objects, otherwise ("", false).
func formatTable(v interface{}) (string, bool) {
	arr, ok := v.([]interface{})
	if !ok || len(arr) == 0 {
		return "", false
	}

	// Check that the first element is a map (object)
	firstObj, ok := arr[0].(map[string]interface{})
	if !ok {
		return "", false
	}

	// Collect column names from the first object, sorted for stability
	columns := make([]string, 0, len(firstObj))
	for k := range firstObj {
		columns = append(columns, k)
	}
	sort.Strings(columns)

	// Compute column widths and collect string values
	widths := make([]int, len(columns))
	for i, col := range columns {
		widths[i] = len(col)
	}

	rows := make([][]string, 0, len(arr))
	for _, item := range arr {
		obj, ok := item.(map[string]interface{})
		if !ok {
			return "", false
		}
		row := make([]string, len(columns))
		for i, col := range columns {
			row[i] = cellValue(obj[col])
			if len(row[i]) > widths[i] {
				widths[i] = len(row[i])
			}
		}
		rows = append(rows, row)
	}

	var sb strings.Builder

	// Header
	for i, col := range columns {
		if i > 0 {
			sb.WriteString("  ")
		}
		sb.WriteString(padRight(strings.ToUpper(col), widths[i]))
	}
	sb.WriteString("\n")

	// Separator
	for i, w := range widths {
		if i > 0 {
			sb.WriteString("  ")
		}
		sb.WriteString(strings.Repeat("-", w))
	}
	sb.WriteString("\n")

	// Data rows
	for _, row := range rows {
		for i, val := range row {
			if i > 0 {
				sb.WriteString("  ")
			}
			sb.WriteString(padRight(val, widths[i]))
		}
		sb.WriteString("\n")
	}

	return strings.TrimRight(sb.String(), "\n"), true
}

// formatCSV attempts to render data as CSV.
// Returns (result, true) if data is an array of objects, otherwise ("", false).
func formatCSV(v interface{}) (string, bool) {
	arr, ok := v.([]interface{})
	if !ok || len(arr) == 0 {
		return "", false
	}

	firstObj, ok := arr[0].(map[string]interface{})
	if !ok {
		return "", false
	}

	columns := make([]string, 0, len(firstObj))
	for k := range firstObj {
		columns = append(columns, k)
	}
	sort.Strings(columns)

	var sb strings.Builder
	w := csv.NewWriter(&sb)

	// Header row
	w.Write(columns)

	// Data rows
	for _, item := range arr {
		obj, ok := item.(map[string]interface{})
		if !ok {
			return "", false
		}
		row := make([]string, len(columns))
		for i, col := range columns {
			row[i] = cellValue(obj[col])
		}
		w.Write(row)
	}
	w.Flush()
	if err := w.Error(); err != nil {
		fmt.Fprintf(os.Stderr, "csv error: %v\n", err)
		return "", false
	}

	return strings.TrimRight(sb.String(), "\n"), true
}

func cellValue(v interface{}) string {
	if v == nil {
		return ""
	}
	switch val := v.(type) {
	case string:
		return val
	case float64:
		// Print integers without decimal point
		if val == float64(int64(val)) {
			return fmt.Sprintf("%d", int64(val))
		}
		return fmt.Sprintf("%g", val)
	case bool:
		if val {
			return "true"
		}
		return "false"
	default:
		b, _ := json.Marshal(val)
		return string(b)
	}
}

func padRight(s string, width int) string {
	if len(s) >= width {
		return s
	}
	return s + strings.Repeat(" ", width-len(s))
}
