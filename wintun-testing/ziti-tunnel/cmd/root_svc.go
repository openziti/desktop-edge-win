package cmd

import (
"fmt"

"github.com/spf13/cobra"
)

func init() {
	rootCmd.AddCommand(serviceCmd)
}

var serviceCmd = &cobra.Command{
	Use:    "service",
	Short:  "Expects to run the program as a service",
	Hidden: true,
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("print or return the version here")
	},
}
