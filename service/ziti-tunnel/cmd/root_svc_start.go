package cmd

import (
"fmt"

"github.com/spf13/cobra"
)

func init() {
	serviceCmd.AddCommand(startCmd)
}

var startCmd = &cobra.Command{
	Use:   "start",
	Short: "Tries to start the service",
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("Tries to start the service")
	},
}
