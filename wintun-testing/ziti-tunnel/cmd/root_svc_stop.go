package cmd

import (
"fmt"

"github.com/spf13/cobra"
)

func init() {
	serviceCmd.AddCommand(stopCmd)
}

var stopCmd = &cobra.Command{
	Use:   "stop",
	Short: "Tries to stop the service",
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("try to stop the service")
	},
}
