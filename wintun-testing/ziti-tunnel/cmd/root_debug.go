package cmd

import (
"fmt"

"github.com/spf13/cobra"
)

func init() {
	rootCmd.AddCommand(debugCmd)
}

var debugCmd = &cobra.Command{
	Use:    "debug",
	Short:  "Runs the program interactively instead of expecting to be run as a service. Used in debugging",
	Hidden: true,
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("Runs the program interactively instead of expecting to be run as a service. Used in debugging")
	},
}
