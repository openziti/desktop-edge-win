package cmd

import (
"fmt"

"github.com/spf13/cobra"
)

func init() {
	rootCmd.AddCommand(addIdCmd)
}

var addIdCmd = &cobra.Command{
	Use:    "addId",
	Short:  "Adds the provided jwt file to the running service",
	Hidden: true,
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("Adds the provided jwt file to the running service")
	},
}
