package cmd

import (
"fmt"

"github.com/spf13/cobra"
)

func init() {
	rootCmd.AddCommand(removeIdCmd)
}

var removeIdCmd = &cobra.Command{
	Use:    "removeId",
	Short:  "Removes any identity from the running service using the provided fingerprint",
	Hidden: true,
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("Removes any identity from the running service using the provided fingerprint")
	},
}
