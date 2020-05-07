package cmd

import (
"fmt"

"github.com/spf13/cobra"
)

func init() {
	serviceCmd.AddCommand(statusCmd)
}

var statusCmd = &cobra.Command{
	Use:    "status",
	Short:  "Prints the status of the service",
	Hidden: true,
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("Prints the status of the service")
	},
}
