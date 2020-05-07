package cmd

import (
"fmt"

"github.com/spf13/cobra"
)

func init() {
	rootCmd.AddCommand(versionCmd)
}

var versionCmd = &cobra.Command{
	Use:   "version",
	Short: "Print the version of the current executable",
	Long:  ``,
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("print or return the version here")
	},
}
